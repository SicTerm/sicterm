using Sic.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Sic.App;

/// <summary>
/// The color of the border that marks the pane with the keyboard (docs/decisions/pane-border.md, October 18, 2026): the theme's
/// accent unless a color is chosen in Settings → Appearance, from the button beside Session Indicator Width, whose flyout is the
/// tab colors' swatches, the theme's own eight, the recent custom colors, Theme accent and Custom…. Kept as <c>PaneBorderColor</c> and shown at once on
/// every pane; while the flyout is open the active pane wears its border even alone in its tab, as it does while the width's
/// slider has the keyboard.
/// </summary>
internal sealed partial class MainWindow
{
    private Button? _paneBorderColorButton;

    /// <summary>The chosen border color, or null for the theme's accent.</summary>
    private Windows.UI.Color? PaneBorderColor => PaneBorder.ResolveColor(_preferences.PaneBorderColor, _theme.Definition) is { } hex ? Theme.Parse(hex) : null;

    /// <summary>The color button at the end of the Session Indicator Width row: a dot of the border's color.</summary>
    private Button BuildPaneBorderColorButton()
    {
        var button = _paneBorderColorButton = _theme.MarkButton(new Border(), "Session indicator color", 32);
        AutomationProperties.SetName(button, "Session Indicator Color");
        button.VerticalAlignment = VerticalAlignment.Bottom;
        button.Margin = new Thickness(0, 0, 0, 4);
        var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };
        // Built as it opens, for the color and the recent custom colors as they are then.
        flyout.Opening += (_, _) => { flyout.Content = PaneBorderColorPanel(flyout); PreviewPaneBorder(true); };
        flyout.Closed += (_, _) => PreviewPaneBorder(false);
        button.Flyout = flyout;
        RepaintPaneBorderColor();
        return button;
    }

    private StackPanel PaneBorderColorPanel(Flyout flyout)
    {
        var current = _preferences.PaneBorderColor;
        void Choose(string? color)
        {
            flyout.Hide();
            try { SetPaneBorderColor(color); }
            catch (Exception exception) { ShowError(exception.Message); }
        }
        var stack = new StackPanel { Spacing = 8, Width = 4 * 32 + 3 * 8 };
        stack.Children.Add(_theme.Eyebrow("Session indicator color"));
        // The presets in their dark shade: the border lies over the terminal, which is dark.
        stack.Children.Add(SwatchGrid(TabColors.Presets.Select(preset => (preset.Name, preset.OnDark, (string?)preset.Id)), current, Choose));
        AddThemeSwatches(stack, current, Choose);
        if (_preferences.RecentTabColors.Count > 0)
        {
            stack.Children.Add(_theme.Tertiary("Recent"));
            stack.Children.Add(SwatchGrid(_preferences.RecentTabColors.Select(hex => ("Custom #" + hex, hex, (string?)hex)), current, Choose));
        }
        stack.Children.Add(_theme.Divider());
        var accent = FlyoutAction("", "Theme accent");
        ToolTipService.SetToolTip(accent, "The accent of the theme in use, which follows a change of theme");
        accent.IsEnabled = current is not null;
        accent.Click += (_, _) => Choose(null);
        stack.Children.Add(accent);
        var custom = FlyoutAction("", "Custom…");
        custom.Click += async (_, _) =>
        {
            flyout.Hide();
            await PickCustomColorAsync("Custom session indicator color", current, color => SetPaneBorderColor(color));
        };
        stack.Children.Add(custom);
        return stack;
    }

    /// <summary>
    /// Settings → Session Indicator Color: a preset id, an RRGGBB, or null for the theme's accent. Saved and shown at once on every
    /// pane, the one with the keyboard wearing it; a custom color joins the recent swatches, as one given to a tab does.
    /// </summary>
    internal void SetPaneBorderColor(string? color)
    {
        if (color is not null && PaneBorder.NormalizeColor(color) is null) throw new ArgumentException("Not a color: " + color, nameof(color));
        _preferences.PaneBorderColor = PaneBorder.NormalizeColor(color);
        if (_preferences.PaneBorderColor is { } chosen && TabColors.IsCustom(chosen)) _preferences.RecentTabColors = TabColors.Remember(_preferences.RecentTabColors, chosen);
        SavePreferences();
        RepaintPaneBorder();
    }

    /// <summary>The border's color on every pane and on the button's dot, as chosen and as the theme has it: a theme color and the accent both change with the theme.</summary>
    private void RepaintPaneBorder()
    {
        var resolved = PaneBorderColor;
        foreach (var pane in AllPanes) pane.RingColor = resolved;
        RefreshActiveRing();
        RepaintPaneBorderColor();
    }

    /// <summary>The button's dot: the chosen color, or the theme's accent.</summary>
    private void RepaintPaneBorderColor()
    {
        if (_paneBorderColorButton is null) return;
        var color = PaneBorderColor ?? _theme.Accent.Color;
        _paneBorderColorButton.Content = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(color), Stroke = _theme.StrokeStrong, StrokeThickness = 1 };
        var name = TabColors.Preset(_preferences.PaneBorderColor)?.Name ?? (TabColors.ThemeRoles.FirstOrDefault(role => role.Id == _preferences.PaneBorderColor) is { } role ? _theme.Definition.Name + " " + role.Name.ToLowerInvariant() : null) ?? (_preferences.PaneBorderColor is { } hex ? "custom #" + hex : "the theme's accent");
        ToolTipService.SetToolTip(_paneBorderColorButton, "Session indicator color: " + name);
    }
}
