using System.Runtime.InteropServices;
using Sic.Core.Appearance;

namespace Sic.App;

/// <summary>
/// The icon SIC hands the taskbar, the title bar and the tray: the shipped one (<see cref="AppIcon.Default"/>), in its
/// light-taskbar or dark-taskbar file as the taskbar's tone changes. Up to October 8, 2026 Settings → App icon offered the
/// design rounds' candidates too (<see cref="AppIcon.All"/>, kept for a later review; docs/decisions/app-icon-options.md);
/// a saved choice is ignored now. The exe's own icon, which File Explorer, Start and shortcuts read, is the shipped one too.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The .ico the taskbar, title-bar and tray icons come from now.</summary>
    private string _appIconPath = "";
    /// <summary>The taskbar's and the menus' tone when the shortcuts and the Explorer menu were last brought into step with them.</summary>
    private (bool LightTaskbar, bool LightMenus)? _shellTone;

    private static string BrandFile(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", "brand", name.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The shipped icon's .ico for a light or a dark taskbar; the exe's own when that file is missing.</summary>
    private string AppIconPath(bool lightTaskbar)
    {
        var path = ShippedIconPath(lightTaskbar);
        return File.Exists(path) ? path : BrandFile("sic.ico");
    }

    private static string ShippedIconPath(bool lightTaskbar) => BrandFile(AppIcon.IconFor(AppIcon.Find(AppIcon.Default), lightTaskbar) + ".ico");

    /// <summary>
    /// Puts the chosen app icon on the taskbar button, the title bar and the tray, and again whenever the taskbar's scaling, size
    /// or light or dark mode may have changed, taking the other file of a one-ink option when the mode flips. The shell's
    /// setting-change broadcast, which brings it here, goes out for any setting from any process (an environment variable, a
    /// locale, a theme), so the shortcuts and the Explorer menu are touched only when the tone they follow has in fact changed:
    /// the shortcuts read every .lnk on the desktop, in Start and among the taskbar's pins.
    /// </summary>
    private void ApplyAppIcon()
    {
        var tone = (LightTaskbar: ShellIcons.LightTaskbar(), LightMenus: ShellIcons.LightMenus());
        var path = AppIconPath(tone.LightTaskbar);
        var changed = !string.Equals(path, _appIconPath, StringComparison.OrdinalIgnoreCase);
        _appIconPath = path;
        if (changed) SetWindowIdentity();
        SetShellIcons(path, force: changed);
        if (changed) _tray?.SetIconPath(path);
        if (_shellTone?.LightTaskbar != tone.LightTaskbar) RetoneShortcuts(tone.LightTaskbar);
        if (_shellTone?.LightMenus != tone.LightMenus) SyncExplorerMenu();
        _shellTone = tone;
    }

    /// <summary>Brings the shortcuts and the Explorer menu into step with the shell's tone at launch, and remembers it for <see cref="ApplyAppIcon"/>.</summary>
    private void FollowShellTone()
    {
        var tone = (LightTaskbar: ShellIcons.LightTaskbar(), LightMenus: ShellIcons.LightMenus());
        SyncExplorerMenu();
        RetoneShortcuts(tone.LightTaskbar);
        _shellTone = tone;
    }

    /// <summary>
    /// Points SIC's own shortcuts at the shipped mark's .ico for a light or a dark taskbar (<see cref="ShellShortcuts"/>): the
    /// shipped icon always, whatever Settings → App icon shows on the window, since the shortcuts keep the shipped icon. Not for a
    /// SIC with its own data folder, whose shortcuts are not its own. The shortcuts are read on a thread-pool task: there may be
    /// dozens, each opened through the shell, and nothing on screen waits for them.
    /// </summary>
    private void RetoneShortcuts(bool lightTaskbar)
    {
        if (_options.Isolated || Environment.ProcessPath is not { } executable) return;
        var iconPath = ShippedIconPath(lightTaskbar);
        _ = Task.Run(() =>
        {
            try { ShellShortcuts.Retone(executable, iconPath); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { /* The shortcuts keep their icon; nothing else depends on this. */ }
        });
    }

    /// <summary>
    /// The shipped icon keeps the window under its shortcut's identity, so the window carries no AppUserModelID of its own
    /// (<see cref="WindowIdentity"/>); a trial icon chosen before October 8, 2026 gave it one, which this clears.
    /// </summary>
    private void SetWindowIdentity()
    {
        try { WindowIdentity.Set(WinRT.Interop.WindowNative.GetWindowHandle(this), null); WindowIdentityId = null; }
        catch (Exception exception) when (exception is COMException or ArgumentException) { ShowError("The taskbar icon could not be set: " + exception.Message); }
    }

    /// <summary>The AppUserModelID the window carries of its own; null, since the shipped icon is the only one.</summary>
    internal string? WindowIdentityId { get; private set; }
}
