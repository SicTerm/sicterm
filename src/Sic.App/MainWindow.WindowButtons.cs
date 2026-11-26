using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Sic.App;

/// <summary>
/// The window's own minimize, maximize and close buttons, for the bars at the bottom (Settings → Tabs), where Windows draws
/// none and the window has no title bar (<see cref="ApplyCaptionButtons"/>); in full screen, where Windows draws none either,
/// the maximize button alone, as Leave full screen (MainWindow.FullScreen.cs). They sit at the end of the tab strip, as Windows'
/// buttons end the strip at the top, and are drawn as Windows 11 draws its own: the caption glyphs, square, the strip's hover,
/// close turning red. Each sends the window the command Windows' button sends, WM_SYSCOMMAND, so whatever answers those
/// answers these: the tray's minimize to the tray, Closing's hide to the tray, the placement tracking. They are plain buttons
/// rather than areas Windows takes the pointer for, so the maximize button opens no snap layouts; Win+Z still does.
/// docs/decisions/tab-bar-side.md has the reasoning.
/// </summary>
internal sealed partial class MainWindow
{
    private const uint SystemCommandMessage = 0x0112;
    private const nuint MinimizeCommand = 0xF020, MaximizeCommand = 0xF030, CloseCommand = 0xF060, RestoreCommand = 0xF120;
    private const string MaximizeGlyph = "", RestoreGlyph = "";
    private readonly StackPanel _windowButtons = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private Button _minimizeButton = null!, _maximizeButton = null!, _closeButton = null!;
    // The glyphs' ink at rest, dimmed while the window is inactive as Windows dims its own; under the pointer it is the strip's.
    private readonly SolidColorBrush _windowButtonInk = new(Microsoft.UI.Colors.Transparent);
    // Windows 11's close button under the pointer and pressed.
    private readonly SolidColorBrush _closeHover = new(Theme.Parse("C42B1C")), _closePressed = new(Theme.Parse("C42B1CE6")), _closeInk = new(Microsoft.UI.Colors.White);
    private bool _windowButtonsActive = true;
    /// <summary>For the smoke: takes the buttons' commands in the window's place.</summary>
    private Action<nuint>? _windowCommandSpy;

    private void BuildWindowButtons()
    {
        _minimizeButton = WindowButton("", "Minimize", danger: false, () => SendWindowCommand(MinimizeCommand));
        // In full screen the maximize button is Leave full screen: SIC's own doing, not a command of Windows'.
        _maximizeButton = WindowButton(MaximizeGlyph, "Maximize", danger: false, () => { if (_fullScreen) SetFullScreen(false); else SendWindowCommand(Maximized ? RestoreCommand : MaximizeCommand); });
        _closeButton = WindowButton("", "Close", danger: true, () => SendWindowCommand(CloseCommand));
        _windowButtons.Children.Add(_minimizeButton); _windowButtons.Children.Add(_maximizeButton); _windowButtons.Children.Add(_closeButton);
    }

    /// <summary>
    /// One button, 46 DIPs wide as Windows' are and the strip's height. The glyph takes the button's ink, which the button's
    /// states set; none takes the focus, as none of Windows' does, so a click leaves the keyboard in the terminal.
    /// </summary>
    private Button WindowButton(string glyph, string name, bool danger, Action press)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 10, FontFamily = Theme.Icons },
            Width = 46, VerticalAlignment = VerticalAlignment.Stretch, Padding = new Thickness(0), CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0), Background = _theme.Transparent, Foreground = _windowButtonInk,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            IsTabStop = false, AllowFocusOnInteraction = false
        };
        Theme.Override(button.Resources, "ButtonBackgroundPointerOver", danger ? _closeHover : _theme.StripHover);
        Theme.Override(button.Resources, "ButtonBackgroundPressed", danger ? _closePressed : _theme.StripPressed);
        Theme.Override(button.Resources, "ButtonForegroundPointerOver", danger ? _closeInk : _theme.StripText);
        Theme.Override(button.Resources, "ButtonForegroundPressed", danger ? _closeInk : _theme.StripText);
        ToolTipService.SetToolTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => press();
        return button;
    }

    private bool Maximized => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

    /// <summary>
    /// The buttons where Windows draws none: all three with the bars at the bottom; in full screen, with the bars on either
    /// edge, only Leave full screen, so the strip keeps its width for tabs, which is what full screen is for.
    /// </summary>
    private void PlaceWindowButtons()
    {
        _windowButtons.Visibility = TabsBottom || _fullScreen ? Visibility.Visible : Visibility.Collapsed;
        _minimizeButton.Visibility = _closeButton.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
        RefreshWindowButtons();
    }

    /// <summary>Posts the command, as Windows' own buttons send theirs, so nothing runs inside the click.</summary>
    private void SendWindowCommand(nuint command)
    {
        if (_windowCommandSpy is { } spy) { spy(command); return; }
        PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(this), SystemCommandMessage, command, 0);
    }

    /// <summary>Follows the window's state and activation, as Windows' own buttons do: maximize and restore arrive as size and position changes.</summary>
    private void TrackWindowButtons()
    {
        AppWindow.Changed += (_, args) => { if (args.DidSizeChange || args.DidPositionChange || args.DidPresenterChange) RefreshWindowButtons(); };
        Activated += (_, args) => { _windowButtonsActive = args.WindowActivationState != WindowActivationState.Deactivated; RefreshWindowButtons(); };
    }

    /// <summary>The maximize button as Restore Down while the window is maximized and Leave full screen while it is full screen, and the ink for the theme and the window's activation.</summary>
    private void RefreshWindowButtons()
    {
        if (_windowButtons.Children.Count == 0) return;
        var maximized = Maximized;
        var name = _fullScreen ? "Leave full screen" : maximized ? "Restore Down" : "Maximize";
        if (_maximizeButton.Content is FontIcon icon) icon.Glyph = maximized || _fullScreen ? RestoreGlyph : MaximizeGlyph;
        ToolTipService.SetToolTip(_maximizeButton, name);
        AutomationProperties.SetName(_maximizeButton, name);
        _windowButtonInk.Color = _windowButtonsActive ? _theme.StripText.Color : Theme.Blend(_theme.StripText.Color, _theme.StripSurface, 0.4);
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
