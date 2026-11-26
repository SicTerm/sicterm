using Sic.Core.Appearance;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Sic.App;

/// <summary>
/// A color per workspace home folder (docs/decisions/folder-colors.md): chosen from Color… on the workspace's menu, and worn by
/// the workspace's indicator at the head of the tab strip (<see cref="PaintWorkspaceChip"/>) and its Sessions group's swatch.
/// It sat on the folder card beside the emoji and the sound, and colored the tabs working in the folder, until November 8, 2026,
/// when the pinned folders became workspaces.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>A flyout at <paramref name="anchor"/> with the folder's tab color choices: the tab's swatches, the recent custom colors, No color and Custom….</summary>
    private void ShowFolderColorFlyout(FrameworkElement anchor, string folder)
    {
        var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };
        flyout.Content = FolderColorPanel(flyout, folder);
        flyout.ShowAt(anchor);
    }

    private StackPanel FolderColorPanel(Flyout flyout, string folder)
    {
        var current = _preferences.FolderColor.GetValueOrDefault(PinnedFolders.Normalize(folder));
        void Choose(string? color)
        {
            flyout.Hide();
            SetFolderColor(folder, color);
        }
        var stack = new StackPanel { Spacing = 8, Width = 4 * 32 + 3 * 8 };
        stack.Children.Add(_theme.Eyebrow("Color for " + Paths.Leaf(folder)));
        stack.Children.Add(SwatchGrid(TabColors.Presets.Select(preset => (preset.Name, ResolveColor(preset.Id, _theme.StripDark)!, (string?)preset.Id)), current, Choose));
        AddThemeSwatches(stack, current, Choose);
        if (_preferences.RecentTabColors.Count > 0)
        {
            stack.Children.Add(_theme.Tertiary("Recent"));
            stack.Children.Add(SwatchGrid(_preferences.RecentTabColors.Select(hex => ("Custom #" + hex, hex, (string?)hex)), current, Choose));
        }
        var hint = _theme.Tertiary("Tints this workspace's indicator at the head of the tab strip.");
        hint.TextWrapping = TextWrapping.Wrap; hint.TextTrimming = TextTrimming.None;
        stack.Children.Add(hint);
        stack.Children.Add(_theme.Divider());
        var none = FlyoutAction("", "No color");
        none.IsEnabled = current is not null;
        none.Click += (_, _) => Choose(null);
        stack.Children.Add(none);
        var custom = FlyoutAction("", "Custom…");
        custom.Click += async (_, _) =>
        {
            flyout.Hide();
            await PickCustomColorAsync("Custom folder color", current, color => SetFolderColor(folder, color));
        };
        stack.Children.Add(custom);
        return stack;
    }

    /// <summary>
    /// Sets or clears a folder's color: saved, and shown at once by its workspace's indicator.
    /// A custom color joins the recent swatches, as one given to a tab does.
    /// </summary>
    internal void SetFolderColor(string folder, string? color)
    {
        color = FolderColors.Normalize(color);
        _preferences.FolderColor = FolderColors.With(_preferences.FolderColor, folder, color);
        if (TabColors.IsCustom(color)) _preferences.RecentTabColors = TabColors.Remember(_preferences.RecentTabColors, color);
        SavePreferences();
        RepaintFolder(folder);
        PaintTabs();
    }

    /// <summary>A stored color as the current theme paints it: a theme color's id takes this theme's shade (<see cref="TabColors.ThemeRoles"/>).</summary>
    private string? ResolveColor(string? value, bool darkStrip) => TabColors.Resolve(value, darkStrip, _theme.Definition);

    /// <summary>
    /// The current theme's own eight colors, under the standard swatches in a color flyout and in their order (since November 12,
    /// 2026). One chosen is kept as its id, so it follows the theme: the next theme paints its own shade of it.
    /// </summary>
    private void AddThemeSwatches(StackPanel stack, string? current, Action<string?> choose)
    {
        var theme = _theme.Definition;
        var label = _theme.Tertiary("From " + theme.Name);
        ToolTipService.SetToolTip(label, "The theme's own colors. One chosen here changes with the theme.");
        stack.Children.Add(label);
        stack.Children.Add(SwatchGrid(TabColors.ThemeRoles.Select(role => (theme.Name + " " + role.Name.ToLowerInvariant(), TabColors.ThemeShade(role.Id, theme)!, (string?)role.Id)), current, choose));
    }

    private readonly Dictionary<Windows.UI.Color, Microsoft.UI.Xaml.Media.SolidColorBrush> _rowBrushes = [];

    /// <summary>
    /// The color a folder's rows show, as the ink of its name and its folder glyph: its own (not the one of a folder above it),
    /// the very color its swatch shows in the Color flyout, the strip's shade of a preset (Chris, November 12, 2026: "what the
    /// person actually picks"); null for a folder without one. Only where that would not read on the panel, a pale color on a
    /// light panel, it deepens until it does (<see cref="Theme.Legible"/>, 3:1). Tried and dropped the same day: a wash over the
    /// row, and a box of the color around the chevron.
    /// </summary>
    internal Windows.UI.Color? RowColor(string? folder) =>
        folder is not null && ResolveColor(FolderColors.Normalize(_preferences.FolderColor.GetValueOrDefault(PinnedFolders.Normalize(folder))), _theme.StripDark) is { } hex
            ? Theme.Legible(Theme.Parse(hex), Theme.Over(_theme.Panel.Color, _theme.Base.Color), 3) : null;

    /// <summary>The color itself as a brush, one per color, kept: the folder's name and glyph on its rows, and the current workspace's bar.</summary>
    private Microsoft.UI.Xaml.Media.SolidColorBrush RowBar(Windows.UI.Color color)
    {
        var solid = Theme.WithAlpha(color, 1);
        if (!_rowBrushes.TryGetValue(solid, out var brush)) _rowBrushes[solid] = brush = new(solid);
        return brush;
    }
}
