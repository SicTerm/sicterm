using Sic.Core.Appearance;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Sic.App;

/// <summary>
/// The live brushes one tab's template reads through the tab's own resources (WinUI lightweight styling), plus the colored
/// stripe along its top edge. The tab's panes paint their headers with <see cref="Selected"/> and the header inks, so the
/// active tab and its panes stay one surface. They are recolored in place for the tab's color, its selection and the theme,
/// never replaced.
/// </summary>
internal sealed class TabPaint
{
    internal SolidColorBrush Idle { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Hover { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Pressed { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Selected { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Ink { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush SelectedInk { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush CloseInk { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Stripe { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>Secondary ink on <see cref="Selected"/>: the pane header's folder and buttons, and the active tab's close button.</summary>
    internal SolidColorBrush HeaderInk { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>Tertiary ink on <see cref="Selected"/>: the pane header's status text.</summary>
    internal SolidColorBrush HeaderInkMuted { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>The pane header's status dot while its agent runs or starts: the theme's success and warning made vivid against <see cref="Selected"/> (StateDots).</summary>
    internal SolidColorBrush HeaderSuccess { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush HeaderWarning { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>
    /// The tab's fill in the drag look a reorder gives it, which the template would take from the Windows theme's solid
    /// background: near white under a light theme, beneath SIC light's white strip ink. The window sets it to a surface the
    /// tab's ink was chosen for (see PaintTab).
    /// </summary>
    internal SolidColorBrush Drag { get; } = new(Microsoft.UI.Colors.Transparent);

    /// <summary>A 3 px top border that follows the tab's rounded top corners and tapers down them.</summary>
    internal Border Bar { get; }
    /// <summary>
    /// The active tab's edge, read by the template as the selected tab's border brush: a hairline round its rounded top and down
    /// its sides into the pane header, in the color PaintTab gives it. Until October 15, 2026 its last 4 px faded out where the
    /// tab's flared feet curved away from the sides; the feet are cut off now (MainWindow.TrimTabFeet).
    /// </summary>
    internal SolidColorBrush Edge { get; } = new(Microsoft.UI.Colors.Transparent);

    internal TabPaint()
    {
        Bar = new Border
        {
            Height = 8, VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(0, 3, 0, 0), CornerRadius = new CornerRadius(7, 7, 0, 0),
            BorderBrush = Stripe, IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        Grid.SetColumnSpan(Bar, 3);
    }

    /// <summary>Must run before the tab is templated: the template resolves these keys once, and later changes only recolor the brushes.</summary>
    internal void Attach(TabViewItem tab)
    {
        tab.Background = Idle;
        var map = new Dictionary<string, Brush>
        {
            ["TabViewItemHeaderBackgroundPointerOver"] = Hover, ["TabViewItemHeaderBackgroundPressed"] = Pressed, ["TabViewItemHeaderBackgroundSelected"] = Selected, ["TabViewSelectedItemBorderBrush"] = Edge,
            ["TabViewItemHeaderDragBackground"] = Drag,
            ["TabViewItemHeaderForeground"] = Ink, ["TabViewItemHeaderForegroundPointerOver"] = Ink, ["TabViewItemHeaderForegroundPressed"] = Ink,
            ["TabViewItemHeaderForegroundSelected"] = SelectedInk, ["TabViewItemHeaderSelectedCloseButtonForeground"] = HeaderInk,
            ["TabViewItemHeaderCloseButtonForegroundPointerOver"] = CloseInk, ["TabViewItemHeaderCloseButtonForegroundPressed"] = CloseInk
        };
        foreach (var (key, brush) in map) Theme.Override(tab.Resources, key, brush);
    }
}

internal sealed partial class MainWindow
{
    // How strongly a tab's color washes it. The active tab carries the color: a strong wash over the pane-header surface, which
    // its pane headers share so the two still read as one surface. An inactive tab keeps only its stripe, washed lightly under
    // the pointer.
    internal const double ActiveWash = 0.3;
    private const double HoverWash = 0.12, PressedWash = 0.09;
    // How much darker than the workspace's colored strip an inactive tab is, at rest, under the pointer and pressed.
    private const double InactiveShade = 0.28, InactiveHoverShade = 0.12, InactivePressedShade = 0.18;

    /// <summary>
    /// The color a tab shows: its own (<see cref="OwnColor"/>). Until November 8, 2026 a tab without one took its folder's
    /// (the nearest listed folder's with one; docs/decisions/folder-colors.md); that color now marks the workspace instead, and
    /// a tab's own <see cref="TabColors.None"/>, saved from then, still paints as no color.
    /// </summary>
    private string? EffectiveColor(SavedTab descriptor) => OwnColor(descriptor);

    /// <summary>The color given to the tab, or to the conversation of a tab of one pane, which comes back when it is reopened; null for none given.</summary>
    private string? OwnColor(SavedTab descriptor) => descriptor.Color ?? (descriptor.Panes.Count == 1 && descriptor.Panes[0].SessionId is { } sessionId
        ? _preferences.ConversationColors.GetValueOrDefault(TabLabels.Key(descriptor.Panes[0].AgentId, sessionId)) : null);

    private void PaintTabs() { foreach (var host in _open.Values) PaintTab(host); }

    /// <summary>An inactive tab's darker shade of the strip's color: solid, or, on a frosted strip, the shade alone, which the part-clear strip shows through.</summary>
    private Windows.UI.Color Shade(double amount, Windows.UI.Color tint) =>
        _theme.Frosted ? Theme.WithAlpha(Microsoft.UI.Colors.Black, amount) : Theme.Over(Theme.WithAlpha(Microsoft.UI.Colors.Black, amount), tint);

    /// <summary>
    /// Recolors one tab for its color, whether it is selected, and the current theme. Inactive titles take the strip's secondary
    /// ink so the active one stands forward. Every ink is checked against the fill it sits on, the active wash included, so any
    /// custom color stays readable in the tab and in its pane headers.
    /// </summary>
    private void PaintTab(TabHost host)
    {
        var paint = host.Paint;
        var hex = ResolveColor(EffectiveColor(host.Saved), _theme.StripDark);
        // On a strip in the workspace's color an inactive tab is a darker shade of it, lighter again under the pointer and
        // pressed (November 11, 2026, at Chris's request); on the theme's own strip it is clear.
        var strip = StripTint ?? _theme.StripSurface;
        paint.Idle.Color = StripTint is { } tint ? Shade(InactiveShade, tint) : Microsoft.UI.Colors.Transparent;
        paint.Selected.Color = _theme.SelectedTab.Color;
        if (hex is null)
        {
            paint.Hover.Color = StripTint is { } hoverTint ? Shade(InactiveHoverShade, hoverTint) : _theme.StripHover.Color;
            paint.Pressed.Color = StripTint is { } pressedTint ? Shade(InactivePressedShade, pressedTint) : _theme.StripPressed.Color;
            paint.Edge.Color = _theme.ActiveTabEdge;
        }
        else
        {
            var color = Theme.Parse(hex);
            paint.Stripe.Color = color;
            paint.Hover.Color = Theme.WithAlpha(color, HoverWash);
            paint.Pressed.Color = Theme.WithAlpha(color, PressedWash);
            paint.Selected.Color = Theme.Over(Theme.WithAlpha(ActiveShade(host.Saved), ActiveWash), _theme.SelectedTab.Color);
            // The edge in the tab's own color, stronger than the wash, so the stripe, the wash and the outline are one color.
            paint.Edge.Color = Theme.WithAlpha(color, 0.55);
        }
        paint.Bar.Visibility = hex is null ? Visibility.Collapsed : Visibility.Visible;
        // An inactive colored tab shows the most color under the pointer, so its ink is chosen against that wash.
        var inactive = Theme.Over(hex is null ? paint.Idle.Color : paint.Hover.Color, strip);
        if (StripTint is not null && hex is not null) inactive = Theme.Over(paint.Hover.Color, paint.Idle.Color);
        paint.Ink.Color = Theme.Readable(inactive, _theme.StripTextSecondary.Color, Theme.Readable(inactive, _theme.StripText.Color, Theme.Ink(inactive)));
        var selected = paint.Selected.Color;
        paint.SelectedInk.Color = Theme.Readable(selected, _theme.Text.Color, Theme.Ink(selected));
        paint.HeaderInk.Color = Theme.Readable(selected, _theme.TextSecondary.Color, paint.SelectedInk.Color);
        paint.HeaderInkMuted.Color = hex is null ? _theme.TextTertiary.Color : Theme.Readable(selected, _theme.TextTertiary.Color, paint.HeaderInk.Color);
        paint.HeaderSuccess.Color = Theme.Vivid(_theme.Success.Color, selected); paint.HeaderWarning.Color = Theme.Vivid(_theme.Warning.Color, selected);
        paint.CloseInk.Color = ReferenceEquals(_tabs.SelectedItem, host.Item) ? paint.SelectedInk.Color : paint.Ink.Color;
        // The drag look hides the selected fill's shape and lays this solid fill under the tab's own wash and ink: the selected
        // fill for the active tab and the strip for the others, which their wash then tints as it does on the strip.
        paint.Drag.Color = ReferenceEquals(_tabs.SelectedItem, host.Item) ? selected : StripTint is null ? _theme.StripSurface : paint.Idle.Color;
    }

    /// <summary>
    /// The shade the active wash is mixed from: a preset's shade for the pane-header surface it tints, which under SIC light (a
    /// dark strip over light panes) is the deeper one, not the strip's. A custom color is used as chosen.
    /// </summary>
    private Windows.UI.Color ActiveShade(SavedTab descriptor) =>
        Theme.Parse(ResolveColor(EffectiveColor(descriptor), darkStrip: Theme.Luminance(_theme.SelectedTab.Color) < 0.4)!);

    /// <summary>Puts the stripe inside the template's TabContainer, the tab's own rounded rectangle, cancelling its padding so the stripe meets the top edge.</summary>
    private static void AttachStripe(TabHost host)
    {
        if (FindNamed<Grid>(host.Item, "TabContainer") is not { } container || ReferenceEquals(host.Paint.Bar.Parent, container)) return;
        (host.Paint.Bar.Parent as Panel)?.Children.Remove(host.Paint.Bar);
        host.Paint.Bar.Margin = new Thickness(-container.Padding.Left, -container.Padding.Top, -container.Padding.Right, 0);
        container.Children.Add(host.Paint.Bar);
    }

    /// <summary>
    /// Cuts off the flared feet WinUI draws at a selected tab's foot: the fill's two curves out past the tab's sides into the
    /// strip, 4 px wide and 4 px tall (the template's SelectedBackgroundPath, laid 4 px past the tab on either side), and the
    /// two arcs of the border color that outline them (LeftRadiusRenderArc, RightRadiusRenderArc). The template's root is
    /// clipped to the tab's own bounds, so the tab is a rounded-top rectangle that runs straight down into its pane header
    /// and takes no room beside itself. The clip is in the root's own coordinates, so it turns over with the tab
    /// (<see cref="MirrorTab"/>). Since October 15, 2026: the feet curled the strip's color back under the tab's sides, and
    /// the 4 px they took made the tab taller than its row.
    /// </summary>
    private static void TrimTabFeet(TabHost host)
    {
        if (FindNamed<Grid>(host.Item, "LayoutRoot") is not { } root || root.Clip is not null) return;
        var clip = new RectangleGeometry();
        root.Clip = clip;
        void Fit() => clip.Rect = new Windows.Foundation.Rect(0, 0, root.ActualWidth, root.ActualHeight);
        root.SizeChanged += (_, _) => Fit();
        Fit();
    }

    /// <summary>
    /// Draws a tab upside down for a strip at the bottom (Settings → Tab bar), or upright again. The template's root is turned
    /// over about its middle, by the scale transform the template gives it, which nothing else sets or animates: the rounded
    /// corners, the colored stripe, and the selected tab's outline and fill all face the other way as one drawing, the parts
    /// the template loads only on selection included. Being inside the tab, the turn is in any image taken of it, such as a
    /// reorder's drag image. The header is turned back inside it, so the agent tile, the title and the dot read as they always
    /// have; the close button's cross reads either way up.
    /// </summary>
    private void MirrorTab(TabHost host)
    {
        var turn = TabsBottom ? -1 : 1;
        if (FindNamed<Grid>(host.Item, "LayoutRoot") is { RenderTransform: ScaleTransform scale } root)
        {
            root.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            scale.ScaleY = turn;
        }
        host.Header.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        host.Header.RenderTransform = TabsBottom ? new ScaleTransform { ScaleY = -1 } : null;
    }

    /// <summary>
    /// The template's close button is 24 px tall, as tall as the tab's content row; it takes the row's height, and like the
    /// header overhangs the 20 px the row is laid into by 4 px at the bottom (TabContentOverhang), keeping its left margin.
    /// </summary>
    private static void FitCloseButton(TabViewItem tab)
    {
        if (FindNamed<Button>(tab, "CloseButton") is not { } close) return;
        close.Height = TabContentHeight;
        close.Margin = new Thickness(close.Margin.Left, 0, close.Margin.Right, -TabContentOverhang);
    }

    // Set when a tab's pointer is cancelled, which is how a reorder takes it over, until EndTabDrag clears the drag look.
    private bool _tabDragged;

    /// <summary>
    /// Clears the drag look a reorder gives the tab it drags once the reorder is over. The template clears it only on the tab's
    /// own pointer release (a cancelled pointer or lost capture leaves it) or on TabDragCompleted, which up to October 10, 2026
    /// a reorder within the strip never raised: it comes only with CanDragTabs, which SIC left off until tabs could be dropped
    /// into panes (MainWindow.TabDrops.cs, which clears the look from that event too). On October 9, 2026 a reordered tab kept
    /// the look, light and unreadable, until it was clicked. The reorder lands as a change to the tab items; a drop that changes
    /// nothing is caught by the window's next pointer move with no button down.
    /// </summary>
    private void WatchTabDrags()
    {
        _tabs.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => _tabDragged = true), handledEventsToo: true);
        _tabs.TabItemsChanged += (_, _) => DispatcherQueue.TryEnqueue(EndTabDrag);
        _root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, args) =>
        {
            if (_tabDragged && !args.GetCurrentPoint(null).Properties.IsLeftButtonPressed) EndTabDrag();
        }), handledEventsToo: true);
    }

    private void EndTabDrag()
    {
        _tabDragged = false;
        foreach (var item in _open.Keys) VisualStateManager.GoToState(item, "DragDropVisualNotVisible", false);
    }

    private static T? FindNamed<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && match.Name == name) return match;
            if (FindNamed<T>(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>Sets or clears a tab's color and, like a local name, stores it against each pane's exact conversation so it comes back when that conversation is reopened.</summary>
    private void SetTabColor(TabHost host, string? color)
    {
        if (color is not null && !TabColors.IsValid(color)) throw new ArgumentException("Not a tab color: " + color, nameof(color));
        color = TabColors.Normalize(color);
        host.Saved = host.Saved with { Color = color };
        host.Item.Tag = host.Saved;
        foreach (var pane in host.Panes)
        {
            if (pane.Descriptor.SessionId is not { } id) continue;
            var key = TabLabels.Key(pane.Descriptor.AgentId, id);
            if (color is null) _preferences.ConversationColors.Remove(key);
            else _preferences.ConversationColors[key] = color;
        }
        if (TabColors.IsCustom(color)) _preferences.RecentTabColors = TabColors.Remember(_preferences.RecentTabColors, color);
        PaintTab(host);
        SavePreferences();
        SaveLayout();
    }

    /// <summary>
    /// Carries a name and color over to a conversation ID learned after they were given: the pane's own name, or, for a tab of
    /// one pane, the tab's (the two are the same thing), and the tab's color. The pane keeps its own name as well, harmlessly,
    /// since the conversation's local name stands above it from here on. The pane's folder is kept for the conversation too:
    /// a new Hermes session is bound a second after it starts, before Hermes has written its folder (sometimes it never does),
    /// and the pane was the only record of it, gone once the tab closed (October 6, 2026: "Folder unknown" and no resume).
    /// </summary>
    private void AdoptIdentity(TabHost host, SessionPane pane, string key)
    {
        var name = pane.Descriptor.CustomTitle ?? (host.Panes.Count == 1 ? host.Saved.CustomTitle : null);
        var folder = pane.Descriptor.WorkingDirectory;
        var newFolder = System.IO.Path.IsPathFullyQualified(folder) && _preferences.ConversationFolders.GetValueOrDefault(key) != folder;
        if (name is null && host.Saved.Color is null && !newFolder) return;
        if (newFolder) _preferences.ConversationFolders[key] = folder;
        if (name is not null) _preferences.ConversationNames[key] = name;
        if (host.Saved.Color is { } color) _preferences.ConversationColors[key] = color;
        SavePreferences();
        pane.RefreshName();
    }

    /// <summary>Turns the tab's title into a text box in place. Enter or clicking away saves, Esc cancels.</summary>
    private void BeginRename(TabHost host)
    {
        if (host.Editor is { } open) { open.Focus(FocusState.Programmatic); return; }
        // Exactly the tab's content row, so renaming does not change the tab's height.
        var editor = InlineRename.Editor(_theme, TabLabel(host.Saved), TabContentHeight, 13, 280, "Tab name", (commit, refocus) => EndRenameAsync(host, commit, refocus));
        // Set before selecting: selecting a tab normally hands focus to its terminal, and OnTabSelected skips that while a rename is open.
        host.Editor = editor;
        _tabs.SelectedItem = host.Item;
        Grid.SetColumn(editor, 2);
        host.Title.Visibility = Visibility.Collapsed;
        host.Header.Children.Add(editor);
    }

    /// <summary>Closes the in-place editor. Only a keyboard finish hands focus back to the terminal; clicking away leaves focus where the click put it.</summary>
    private async Task EndRenameAsync(TabHost host, bool commit, bool refocus)
    {
        if (host.Editor is not { } editor) return;
        host.Editor = null;
        var name = editor.Text.Trim();
        host.Header.Children.Remove(editor);
        host.Title.Visibility = Visibility.Visible;
        try
        {
            if (commit && name.Length > 0 && name != TabLabel(host.Saved) && _open.ContainsKey(host.Item))
            {
                SetCustomTabTitle(host.Item, name);
                await SearchAsync();
            }
        }
        catch (Exception exception) { ShowError(exception.Message); }
        if (refocus && ReferenceEquals(SelectedHost, host) && _activePane is { IsRunning: true, Terminal: { } terminal } && host.Panes.Contains(_activePane))
            _ = terminal.FocusInputAsync();
    }

    /// <summary>The swatch grid: eight presets in the shade the strip will show, recent custom colors, No color, and Custom….</summary>
    private void ShowColorFlyout(TabHost host)
    {
        if (!_open.ContainsKey(host.Item)) return;
        // Towards the panes, which with the tabs at the bottom is upwards.
        var flyout = new Flyout { Placement = TabsBottom ? FlyoutPlacementMode.TopEdgeAlignedLeft : FlyoutPlacementMode.BottomEdgeAlignedLeft };
        var current = TabColors.Normalize(EffectiveColor(host.Saved));
        void Choose(string? color)
        {
            flyout.Hide();
            try { SetTabColor(host, color); }
            catch (Exception exception) { ShowError(exception.Message); }
        }
        var stack = new StackPanel { Spacing = 8, Width = 4 * 32 + 3 * 8 };
        stack.Children.Add(_theme.Eyebrow("Tab color"));
        stack.Children.Add(SwatchGrid(TabColors.Presets.Select(preset => (preset.Name, ResolveColor(preset.Id, _theme.StripDark)!, (string?)preset.Id)), current, Choose));
        AddThemeSwatches(stack, current, Choose);
        if (_preferences.RecentTabColors.Count > 0)
        {
            stack.Children.Add(_theme.Tertiary("Recent"));
            stack.Children.Add(SwatchGrid(_preferences.RecentTabColors.Select(hex => ("Custom #" + hex, hex, (string?)hex)), current, Choose));
        }
        stack.Children.Add(_theme.Divider());
        var none =FlyoutAction("", "No color");
        none.IsEnabled = current is not null && current != TabColors.None;
        none.Click += (_, _) => Choose(null);
        stack.Children.Add(none);
        var custom = FlyoutAction("", "Custom…");
        custom.Click += async (_, _) => { flyout.Hide(); await PickCustomColorAsync(host); };
        stack.Children.Add(custom);
        flyout.Content = stack;
        flyout.ShowAt(host.Item);
    }

    private Grid SwatchGrid(IEnumerable<(string Name, string Hex, string? Value)> swatches, string? current, Action<string?> choose)
    {
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var column = 0; column < 4; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        var index = 0;
        foreach (var (name, hex, value) in swatches)
        {
            if (index % 4 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            var color = Theme.Parse(hex);
            var dot = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(color),
                BorderBrush = _theme.StrokeStrong, BorderThickness = new Thickness(1),
                Child = value == current ? Theme.Icon("", 12, new SolidColorBrush(Theme.Ink(color))) : null
            };
            var button = new Button
            {
                Content = dot, Width = 32, Height = 32, Padding = new Thickness(0), CornerRadius = new CornerRadius(16),
                Background = _theme.Transparent, BorderThickness = new Thickness(0)
            };
            ToolTipService.SetToolTip(button, name);
            AutomationProperties.SetName(button, value == current ? name + ", current" : name);
            button.Click += (_, _) => choose(value);
            Grid.SetRow(button, index / 4); Grid.SetColumn(button, index % 4);
            grid.Children.Add(button);
            index++;
        }
        return grid;
    }

    private static Button FlyoutAction(string glyph, string text)
    {
        var button = Theme.IconTextButton(glyph, text);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderThickness = new Thickness(0);
        return button;
    }

    /// <summary>A full color picker for an exact color. The result is painted as chosen on every theme and joins the recent swatches.</summary>
    private Task PickCustomColorAsync(TabHost host) => PickCustomColorAsync("Custom tab color", EffectiveColor(host.Saved), color =>
    {
        if (_open.ContainsKey(host.Item)) SetTabColor(host, color);
    });

    /// <summary>The color picker dialog, starting from a stored color or the accent; <paramref name="use"/> gets the RRGGBB chosen.</summary>
    private async Task PickCustomColorAsync(string title, string? stored, Action<string> use)
    {
        try
        {
            var start = ResolveColor(stored, _theme.StripDark) ?? Theme.Hex(_theme.Accent.Color)[1..];
            var picker = new ColorPicker
            {
                Color = Theme.Parse(start), IsAlphaEnabled = false, IsColorPreviewVisible = true, IsHexInputVisible = true,
                IsColorChannelTextInputVisible = false, IsMoreButtonVisible = false, ColorSpectrumShape = ColorSpectrumShape.Ring
            };
            var dialog = Dialog(title, picker, "Use color");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            use($"{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}");
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }
}
