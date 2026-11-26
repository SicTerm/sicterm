using System.Text.Json;
using Sic.Core.Agents;
using Sic.Core.Appearance;
using Sic.Core.Shell;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Sic.App;

/// <summary>
/// The Settings panel, in four tabs along its bottom edge (SettingsTabs): General (theme catalog, app icon, alerts,
/// application), Terminal (font and options), Agents (commands and history homes, the usage limits), About (version, console
/// host, updates, local data, shortcuts), and a fifth button in the same bar, Quit.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The Theme drop list with its items by id and, under the theme buttons, the theme files that could not be used.</summary>
    private CatalogPicker _themes = null!;
    private ComboBox _themePicker => _themes.Box;
    private IReadOnlyDictionary<string, ComboBoxItem> _themeItems => _themes.Items;
    private TextBlock _themeProblems => _themes.Problems;
    private Border _themeSwatch = null!, _themeSwatchDot = null!;
    private ScrollViewer _settingsScroll = null!;

    private UIElement BuildSettingsPanel()
    {
        StackPanel Page() => new() { Spacing = 12, Padding = new Thickness(16, 0, 16, 20 + PanelFootBuffer) };
        var general = Page(); var appearance = Page(); var terminal = Page(); var agents = Page(); var about = Page();
        // The sections are built in the order they always were, each into its tab's page.
        var stack = appearance;
        stack.Children.Add(Section("Appearance"));
        // One drop list for every theme, in the catalog's order, which keeps each family together: a list that thirty themes
        // fit, where thirty radio rows would not. The swatch beside it is the chosen theme's surface
        // and accent; the items are names alone, since a WinUI ComboBox shows an item that holds elements blank in its list
        // while that item is the one selected.
        _themes = new CatalogPicker(_theme, "Theme", StringComparer.Ordinal);
        FillThemePicker();
        _themes.Box.SelectionChanged += (_, _) =>
        {
            if (!_themes.Syncing && _themes.SelectedId is { } id && _preferences.Theme != id) ApplyTheme(id);
        };
        _themeSwatchDot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _themeSwatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Bottom, Child = _themeSwatchDot };
        var themeRow = new Grid { ColumnSpacing = 8 };
        themeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        themeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        themeRow.Children.Add(_themePicker);
        Grid.SetColumn(_themeSwatch, 1); themeRow.Children.Add(_themeSwatch);
        stack.Children.Add(themeRow);
        RefreshThemePicker();
        // Themes as files (docs/decisions/theme-files.md): a theme of the user's own is a .json file in the themes folder, listed
        // after the built-in ones. Export writes the chosen theme there as a complete starting point.
        var themeTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var themesFolder = Theme.IconTextButton("", "Themes");
        themesFolder.Click += (_, _) => OpenThemesFolder();
        var reloadThemes = Theme.IconTextButton("", "Reload");
        reloadThemes.Click += (_, _) => ReloadThemes(notify: true);
        var copyTheme = Theme.IconTextButton("", "Export", "Save this theme as a .json file to edit");
        copyTheme.Click += (_, _) => CopyThemeToFile();
        themeTools.Children.Add(themesFolder); themeTools.Children.Add(reloadThemes); themeTools.Children.Add(copyTheme);
        stack.Children.Add(themeTools);
        stack.Children.Add(_themes.Problems);
        RefreshThemeProblems();
        var motion = new ComboBox { Header = "Menu Animation", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in MotionSetting.Choices) motion.Items.Add(new ComboBoxItem { Content = MotionSetting.Label(choice), Tag = choice });
        motion.SelectedItem = motion.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _preferences.Motion);
        motion.SelectionChanged += (_, _) => { if (motion.SelectedItem is ComboBoxItem { Tag: string choice }) SetMotion(choice); };
        stack.Children.Add(motion);
        var sidebar = new ComboBox { Header = "Side Panel", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var side in SidebarSide.Choices) sidebar.Items.Add(new ComboBoxItem { Content = SidebarSide.Label(side), Tag = side });
        sidebar.SelectedItem = sidebar.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _preferences.Sidebar);
        sidebar.SelectionChanged += (_, _) => { if (sidebar.SelectedItem is ComboBoxItem { Tag: string side }) SetSidebarSide(side); };
        stack.Children.Add(sidebar);
        var autoHide = new ToggleSwitch { Header = "Autohide sidebar", IsOn = _preferences.SidebarAutoHide };
        autoHide.Toggled += (_, _) => SetSidebarAutoHide(autoHide.IsOn);
        Explain(autoHide, "With the panel closed, the sidebar hides until the pointer reaches the window's edge, as in full screen");
        stack.Children.Add(autoHide);
        var chrome = new ComboBox { Header = "Chrome", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var style in ChromeStyle.Choices) chrome.Items.Add(new ComboBoxItem { Content = ChromeStyle.Label(style), Tag = style });
        chrome.SelectedItem = chrome.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _preferences.Chrome);
        chrome.SelectionChanged += (_, _) => { if (chrome.SelectedItem is ComboBoxItem { Tag: string style }) SetChrome(style); };
        Explain(chrome, "Frosted: the sidebar, the panel, the tab strip and the status bar show the desktop behind SicTerm, blurred; over Remote Desktop, Windows draws plain colors instead. Clear: the same with no blur. The terminals stay solid.");
        stack.Children.Add(chrome);
        // The whole window's opacity, applied as the slider moves (MainWindow.Opacity.cs).
        var opacity = new Slider
        {
            Header = "Window opacity", Minimum = WindowOpacityMin, Maximum = 100, StepFrequency = 1, SmallChange = 1, LargeChange = 5,
            Value = _preferences.WindowOpacity, IsThumbToolTipEnabled = false
        };
        opacity.ValueChanged += (_, args) => SetWindowOpacity((int)Math.Round(args.NewValue));
        Explain(opacity, "Below 100%, the desktop behind SicTerm shows through the whole window, the terminals and their text included. It shows over Remote Desktop too.");
        _windowOpacityValue = _theme.Caption(_preferences.WindowOpacity + "%");
        _windowOpacityValue.VerticalAlignment = VerticalAlignment.Bottom;
        _windowOpacityValue.Margin = new Thickness(0, 0, 0, 8);
        var opacityRow = new Grid { ColumnSpacing = 8 };
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityRow.Children.Add(opacity);
        Grid.SetColumn(_windowOpacityValue, 1); opacityRow.Children.Add(_windowOpacityValue);
        stack.Children.Add(opacityRow);
        var tabBar = new ComboBox { Header = "Tabs", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var side in TabBarSide.Choices) tabBar.Items.Add(new ComboBoxItem { Content = TabBarSide.Label(side), Tag = side });
        tabBar.SelectedItem = tabBar.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _preferences.TabBar);
        tabBar.SelectionChanged += (_, _) => { if (tabBar.SelectedItem is ComboBoxItem { Tag: string side }) SetTabBarSide(side); };
        Explain(tabBar, "Bottom: the tabs, each pane's header and the window buttons along the bottom, and no title bar");
        stack.Children.Add(tabBar);
        // Whole pixels on a slider with the width beside it, applied as it moves (docs/decisions/pane-border.md). While the slider
        // has the keyboard, the pane with the keyboard before it wears the border even alone in its tab, so the width shows.
        var paneBorder = _paneBorderSlider = new Slider
        {
            Header = "Session Indicator Width", Minimum = PaneBorder.Min, Maximum = PaneBorder.Max, StepFrequency = 1, SmallChange = 1, LargeChange = 1,
            Value = _preferences.PaneBorderWidth, IsThumbToolTipEnabled = false
        };
        paneBorder.ValueChanged += (_, args) => SetPaneBorder((int)Math.Round(args.NewValue));
        paneBorder.GotFocus += (_, _) => PreviewPaneBorder(true);
        paneBorder.LostFocus += (_, _) => PreviewPaneBorder(false);
        Explain(paneBorder, "The accent line round the pane that has the keyboard, in a tab of two or more panes");
        _paneBorderValue = _theme.Caption(PaneBorder.Label(_preferences.PaneBorderWidth));
        _paneBorderValue.VerticalAlignment = VerticalAlignment.Bottom;
        _paneBorderValue.Margin = new Thickness(0, 0, 0, 8);
        var paneBorderRow = new Grid { ColumnSpacing = 8 };
        paneBorderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        paneBorderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        paneBorderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        paneBorderRow.Children.Add(paneBorder);
        Grid.SetColumn(_paneBorderValue, 1); paneBorderRow.Children.Add(_paneBorderValue);
        // The border's color, at the row's end: a dot of it, whose flyout is the tab colors' swatches (MainWindow.PaneBorderColor.cs).
        var paneBorderColor = BuildPaneBorderColorButton();
        Grid.SetColumn(paneBorderColor, 2); paneBorderRow.Children.Add(paneBorderColor);
        stack.Children.Add(paneBorderRow);
        // The zoomed pane's card (Ctrl+Shift+Z, MainWindow.Zoom.cs): what it is a share of, and how big a share, applied at once.
        var zoom = new ComboBox { Header = "Zoomed pane", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var frame in PaneZoom.Choices) zoom.Items.Add(new ComboBoxItem { Content = PaneZoom.Label(frame), Tag = frame });
        zoom.SelectedItem = zoom.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _preferences.PaneZoom);
        zoom.SelectionChanged += (_, _) => { if (zoom.SelectedItem is ComboBoxItem { Tag: string frame }) SetPaneZoom(frame, _preferences.PaneZoomPercent); };
        Explain(zoom, "Ctrl+Shift+Z shows the focused pane as a card over the others, and puts it back. Screen: a share of the display, whatever the window's size; a window too small for it fills the display while zoomed. Window: a share of the whole window, over the side panel too. Terminal area: a share of the room the panes have.");
        stack.Children.Add(zoom);
        var zoomSize = new Slider
        {
            Header = "Zoomed pane size", Minimum = PaneZoom.MinPercent, Maximum = PaneZoom.MaxPercent, StepFrequency = 5, SmallChange = 5, LargeChange = 10,
            Value = _preferences.PaneZoomPercent, IsThumbToolTipEnabled = false
        };
        zoomSize.ValueChanged += (_, args) => SetPaneZoom(_preferences.PaneZoom, (int)Math.Round(args.NewValue));
        _paneZoomValue = _theme.Caption(PaneZoom.PercentLabel(_preferences.PaneZoomPercent));
        _paneZoomValue.VerticalAlignment = VerticalAlignment.Bottom;
        _paneZoomValue.Margin = new Thickness(0, 0, 0, 8);
        var zoomSizeRow = new Grid { ColumnSpacing = 8 };
        zoomSizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        zoomSizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        zoomSizeRow.Children.Add(zoomSize);
        Grid.SetColumn(_paneZoomValue, 1); zoomSizeRow.Children.Add(_paneZoomValue);
        stack.Children.Add(zoomSizeRow);

        stack = terminal;
        stack.Children.Add(Section("Terminal"));
        var font = new ComboBox { Header = "Font family", IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var candidate in TerminalOptions.FontFamilies) font.Items.Add(candidate);
        font.Text = _preferences.FontFamily;
        if (font.Items.Contains(_preferences.FontFamily)) font.SelectedItem = _preferences.FontFamily;
        font.SelectionChanged += async (_, _) => { if (font.SelectedItem is string chosen) await ApplyFontAsync(chosen, _preferences.FontSize); };
        font.TextSubmitted += async (_, args) => { args.Handled = true; await ApplyFontAsync(args.Text, _preferences.FontSize); };
        Explain(font, "Or type any installed font");
        stack.Children.Add(font);
        var size = new NumberBox { Header = "Font size", Minimum = 8, Maximum = 48, Value = _preferences.FontSize, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 1 };
        size.ValueChanged += async (_, args) => { if (double.IsFinite(args.NewValue) && (int)args.NewValue != _preferences.FontSize) await ApplyFontAsync(_preferences.FontFamily, (int)args.NewValue); };
        stack.Children.Add(size);
        var cursor = new ComboBox { Header = "Cursor", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var style in TerminalOptions.CursorStyles) cursor.Items.Add(new ComboBoxItem { Content = TerminalOptions.CursorLabel(style), Tag = style });
        cursor.SelectedItem = cursor.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _preferences.CursorStyle);
        cursor.SelectionChanged += async (_, _) => { if (cursor.SelectedItem is ComboBoxItem { Tag: string style }) await ApplyTerminalOptionsAsync(preferences => preferences.CursorStyle = style); };
        stack.Children.Add(cursor);
        var blink = new ToggleSwitch { Header = "Cursor blink", IsOn = _preferences.CursorBlink };
        blink.Toggled += async (_, _) => await ApplyTerminalOptionsAsync(preferences => preferences.CursorBlink = blink.IsOn);
        stack.Children.Add(blink);
        var scrollback = new NumberBox
        {
            Header = "Scrollback lines per pane", Minimum = TerminalOptions.MinScrollback, Maximum = TerminalOptions.MaxScrollback, Value = _preferences.Scrollback,
            SmallChange = 1000, LargeChange = 10000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        scrollback.ValueChanged += async (_, args) =>
        {
            if (double.IsFinite(args.NewValue) && (int)args.NewValue != _preferences.Scrollback) await ApplyTerminalOptionsAsync(preferences => preferences.Scrollback = (int)args.NewValue);
        };
        stack.Children.Add(scrollback);
        var copyOnSelect = new ToggleSwitch { Header = "Copy on select", IsOn = _preferences.CopyOnSelect };
        copyOnSelect.Toggled += async (_, _) => await ApplyTerminalOptionsAsync(preferences => preferences.CopyOnSelect = copyOnSelect.IsOn);
        stack.Children.Add(copyOnSelect);
        var confirmPaste = _confirmPasteSwitch = new ToggleSwitch { Header = "Confirm multi-line pastes into PowerShell", IsOn = _preferences.ConfirmMultiLinePaste };
        confirmPaste.Toggled += (_, _) => SetConfirmMultiLinePaste(confirmPaste.IsOn);
        Explain(confirmPaste, "PowerShell runs each pasted line at once");
        stack.Children.Add(confirmPaste);
        var petImages = new ToggleSwitch { Header = "Pet images", IsOn = _preferences.PetImages };
        petImages.Toggled += async (_, _) => await ApplyTerminalOptionsAsync(preferences => preferences.PetImages = petImages.IsOn);
        Explain(petImages, "Hermes's pet as an image, in kitty render mode");
        stack.Children.Add(petImages);

        BuildAgentsPage(agents);

        stack = about;
        stack.Children.Add(Section("About"));
        stack.Children.Add(BuildAbout(tagline: true));
        _consoleHostText = _theme.Tertiary(ConsoleHostLine());
        _consoleHostText.TextWrapping = TextWrapping.Wrap; _consoleHostText.TextTrimming = TextTrimming.None;
        stack.Children.Add(_consoleHostText);
        stack.Children.Add(Section("Local data"));
        var dataPath = _theme.MonoText(_options.DataDirectory); dataPath.TextWrapping = TextWrapping.Wrap; dataPath.TextTrimming = TextTrimming.None;
        Explain(dataPath, "Settings, layout, search index and log. Agent histories are never modified.");
        stack.Children.Add(dataPath);
        var dataButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var refresh = Theme.IconTextButton("", "Refresh history");
        refresh.Click += async (_, _) => await RefreshHistoryAsync();
        var openFolder = Theme.IconTextButton("", "Open data folder");
        openFolder.Click += (_, _) => Paths.OpenInExplorer(_options.DataDirectory);
        dataButtons.Children.Add(refresh); dataButtons.Children.Add(openFolder);
        stack.Children.Add(dataButtons);

        stack.Children.Add(Section("Keyboard"));
        // The reference itself is the F1 dialog's; here one button opens it, rather than the same twenty-five rows again.
        var shortcuts = Theme.IconTextButton("", "Keyboard shortcuts", "F1");
        shortcuts.Click += async (_, _) => await ShowKeyboardHelpAsync();
        stack.Children.Add(shortcuts);

        stack.Children.Add(Section("Welcome"));
        // The first-run film and card again, with the tour over the live window from its card (docs/decisions/first-run.md).
        var welcome = Theme.IconTextButton("", "Welcome tour", "The film shown on the first run, then a tour of the window");
        welcome.Click += async (_, _) => await ShowWelcomeAsync();
        stack.Children.Add(welcome);

        stack = general;
        stack.Children.Add(Section("Application"));
        BuildRunAtStartupSwitch(stack);
        var closeAndMinimize = new ComboBox { Header = "Close and minimize", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in CloseAndMinimize.Choices) closeAndMinimize.Items.Add(new ComboBoxItem { Content = CloseAndMinimize.Label(choice), Tag = choice });
        closeAndMinimize.SelectedItem = closeAndMinimize.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _preferences.CloseAndMinimize);
        closeAndMinimize.SelectionChanged += (_, _) => { if (closeAndMinimize.SelectedItem is ComboBoxItem { Tag: string choice }) SetCloseAndMinimize(choice); };
        Explain(closeAndMinimize, "In the tray, SicTerm and its sessions keep running");
        stack.Children.Add(closeAndMinimize);
        BuildExplorerMenuSwitch(stack);
        var alwaysExitCleanly = new ToggleSwitch { Header = "Always exit cleanly", IsOn = _preferences.AlwaysExitCleanly };
        alwaysExitCleanly.Toggled += (_, _) => SetAlwaysExitCleanly(alwaysExitCleanly.IsOn);
        Explain(alwaysExitCleanly, "Send each agent its exit command on close, without asking");
        stack.Children.Add(alwaysExitCleanly);
        var resumeAllOnLaunch = new ToggleSwitch { Header = "Resume sessions on launch", IsOn = _preferences.ResumeAllOnLaunch };
        resumeAllOnLaunch.Toggled += (_, _) => SetResumeAllOnLaunch(resumeAllOnLaunch.IsOn);
        Explain(resumeAllOnLaunch, "Off: resume with F5");
        stack.Children.Add(resumeAllOnLaunch);
        stack.Children.Add(Section("Alerts"));
        BuildSoundSettings(stack);
        // Updates follows About's version and console host lines, ahead of Local data and the keyboard reference.
        var updates = new StackPanel { Spacing = 12 };
        AddUpdatesSettings(updates);
        about.Children.Insert(3, updates);

        _settingsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_settingsScroll);
        var bar = _settingsBar = new Grid { BorderBrush = _theme.Stroke, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(4, 2, 4, 4) };
        bar.SizeChanged += (_, _) => PlacePanelFoot();
        Grid.SetRow(bar, 1); root.Children.Add(bar);
        var pages = new Dictionary<string, StackPanel> { [SettingsTabs.General] = general, [SettingsTabs.Appearance] = appearance, [SettingsTabs.Terminal] = terminal, [SettingsTabs.Agents] = agents, [SettingsTabs.About] = about };
        // A button of the bar, in the next column: an icon over its name, with the rail's selection pill turned on its side above
        // them, so the panel's tabs mark the open one the way the rail marks the open panel. Quit has the pill too, never shown.
        (Button Button, Border Pill, FontIcon Icon, TextBlock Label) BarButton(string glyph, string text, string name)
        {
            var icon = Theme.Icon(glyph, 16, _theme.TextSecondary);
            var label = _theme.BodyText(text, 11); label.Foreground = _theme.TextSecondary; label.HorizontalAlignment = HorizontalAlignment.Center;
            var face = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            face.Children.Add(icon); face.Children.Add(label);
            var pill = new Border { Width = 16, Height = 3, CornerRadius = new CornerRadius(2), Background = _theme.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            var content = new Grid { Height = 48 };
            content.Children.Add(pill); content.Children.Add(face);
            var button = new Button
            {
                Content = content, Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(5), Background = _theme.Transparent, BorderThickness = new Thickness(0)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(button, bar.ColumnDefinitions.Count - 1); bar.Children.Add(button);
            return (button, pill, icon, label);
        }
        foreach (var id in SettingsTabs.Choices)
        {
            var glyph = id switch { SettingsTabs.Appearance => "", SettingsTabs.Terminal => "", SettingsTabs.Agents => "", SettingsTabs.About => "", _ => "" };
            var (button, pill, icon, label) = BarButton(glyph, SettingsTabs.Label(id), SettingsTabs.Label(id) + " settings");
            button.Click += (_, _) => ShowSettingsTab(id);
            _settingsTabs[id] = (button, pill, icon, label, pages[id]);
        }
        // Quit, the bar's fifth button with the tabs' face (the power glyph over Quit): it quits as the tray menu's Quit does,
        // asking about live agents. It was a button at the end of General until October 15, 2026; here it is always in reach.
        var (quit, _, _, _) = BarButton("", "Quit", "Quit SicTerm");
        quit.Click += async (_, _) => await CloseAsync();
        ShowSettingsTab(_preferences.SettingsTab, persist: false);
        return root;
    }

    private readonly Dictionary<string, (Button Button, Border Pill, FontIcon Icon, TextBlock Label, StackPanel Page)> _settingsTabs = new(StringComparer.Ordinal);
    private TextBlock? _consoleHostText;
    private bool _syncingExplorerMenuSwitch;

    /// <summary>
    /// Settings → General → Open in SicTerm Explorer Context Menu. Its state is the registry's, not settings.json's, so it tells the
    /// truth after an update or when another copy of SIC has the entry: on when the entry points at this exe, otherwise off,
    /// saying which copy has it. On registers this copy; off removes the entry, whichever copy it pointed at. A copy with a data
    /// folder of its own (the smoke) shows the switch disabled and leaves the real menu alone.
    /// </summary>
    private void BuildExplorerMenuSwitch(StackPanel stack)
    {
        var executable = Environment.ProcessPath ?? "";
        string? registered = null;
        string? problem = null;
        var mode = ExplorerMenuMode.None;
        try { registered = ExplorerMenuRegistration.RegisteredExecutable(); mode = ExplorerMenuRegistration.Mode(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { problem = exception.Message; }
        var toggle = new ToggleSwitch { Header = "Open in SicTerm Explorer Context Menu", OnContent = "On", OffContent = "Off", IsOn = OwnsExplorerMenu(registered), IsEnabled = !_options.Isolated && problem is null };
        // On and Off, with the facts that are not: where the entry is, and another copy holding it.
        void Describe(string? pointsAt, ExplorerMenuMode current) => Explain(toggle,
            pointsAt is not null ? "Points at another copy: " + pointsAt
            : current == ExplorerMenuMode.Classic ? "Under Show more options" : null);
        Describe(OwnsExplorerMenu(registered) ? null : registered, mode);
        toggle.Toggled += async (_, _) =>
        {
            if (_syncingExplorerMenuSwitch) return;
            try
            {
                if (toggle.IsOn)
                {
                    var result = await ExplorerMenuRegistration.RegisterAsync(executable, _preferences.Agents, IconSourceFor, LibraryIconFor);
                    Describe(null, result.Mode);
                    if (result.PackageProblem is { } packageProblem)
                    {
                        // Windows' own words go to the log; the notice says the one thing a reader can act on. The untrusted
                        // certificate (0x800B0109) is the usual case on a PC that has not imported it (README, Install and update).
                        Diagnostics.Log("explorer menu package: " + packageProblem);
                        Notify(InfoBarSeverity.Warning, "Open in SicTerm", packageProblem.Contains("0x800B0109", StringComparison.OrdinalIgnoreCase)
                            ? "This PC does not trust the certificate that puts Open in SicTerm in Windows 11's own right-click menu. For now the entry is under Show more options."
                            : "Windows 11's own menu was not available, so the entry is under Show more options: " + packageProblem);
                    }
                    else if (result.Mode == ExplorerMenuMode.Package)
                        NotifyBriefly("Open in SicTerm", "Added to File Explorer's menu. It may need a File Explorer restart or a new sign-in to appear.");
                }
                else
                {
                    await ExplorerMenuRegistration.UnregisterAsync();
                    Describe(null, ExplorerMenuMode.None);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ShowError("The File Explorer menu could not be changed: " + exception.Message);
                _syncingExplorerMenuSwitch = true;
                try { toggle.IsOn = !toggle.IsOn; } finally { _syncingExplorerMenuSwitch = false; }
            }
        };
        stack.Children.Add(toggle);
        // A disabled switch shows no tooltip, so the two reasons it can be disabled are said on the page.
        if (problem is not null || _options.Isolated)
        {
            var note = _theme.Tertiary(problem is not null ? "The registry could not be read: " + problem : "Not offered to a SicTerm started with its own data folder (--data-dir).");
            note.TextWrapping = TextWrapping.Wrap; note.TextTrimming = TextTrimming.None;
            stack.Children.Add(note);
        }
    }

    /// <summary>Settings → About's line on the console host under the terminals (docs/decisions/console-host.md).</summary>
    private static string ConsoleHostLine() => ConsoleHost.Current == ConsoleHost.Packaged
        ? "Console host: OpenConsole " + ConsoleHost.PackagedVersion + ", Microsoft's packaged host, shipped with SicTerm"
        : "Console host: Windows' own, because " + ConsoleHost.FallbackReason;

    /// <summary>Shows a Settings tab from its top, marks it in the bar, and remembers it unless restoring.</summary>
    internal void ShowSettingsTab(string tab, bool persist = true)
    {
        tab = SettingsTabs.Normalize(tab);
        foreach (var (id, item) in _settingsTabs)
        {
            var open = id == tab;
            item.Pill.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            item.Icon.Foreground = item.Label.Foreground = open ? _theme.Text : _theme.TextSecondary;
            item.Label.FontWeight = open ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
        // A start can fall back to Windows' console host after the panel was built.
        if (tab == SettingsTabs.About && _consoleHostText is { } host) host.Text = ConsoleHostLine();
        if (!ReferenceEquals(_settingsScroll.Content, _settingsTabs[tab].Page))
        {
            _settingsScroll.Content = _settingsTabs[tab].Page;
            if (_settingsScroll.IsLoaded) _settingsScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        if (persist && _preferences.SettingsTab != tab) { _preferences.SettingsTab = tab; SavePreferences(); }
    }

    /// <summary>
    /// The mark and the wordmark, with the version: Settings → About, and the head of the F1 dialog. With
    /// <paramref name="tagline"/>, the line under the wordmark says what Sic stands for, Super-Intelligent-Chat Terminal; About,
    /// the welcome page (on two lines, <see cref="BuildWelcome"/>) and the website are the only places that say it, and everywhere
    /// else the name is just SicTerm.
    /// </summary>
    private Grid BuildAbout(bool tagline)
    {
        var about = new Grid { ColumnSpacing = 12 };
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var aboutMark = Sic.App.Controls.SicMark.Create(48, () => Theme.Over(_theme.Panel.Color, _theme.Base.Color)); aboutMark.VerticalAlignment = VerticalAlignment.Top;
        about.Children.Add(aboutMark);
        var aboutText = new StackPanel { Spacing = 2 };
        aboutText.Children.Add(Sic.App.Controls.SicMark.Wordmark(_theme, 18));
        if (tagline)
            aboutText.Children.Add(new TextBlock
            {
                Text = "Super-Intelligent-Chat Terminal", FontFamily = Theme.AzeretMono, FontSize = 11, Foreground = _theme.TextSecondary,
                FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, Margin = new Thickness(0, 4, 0, 2), TextWrapping = TextWrapping.Wrap
            });
        var version = typeof(MainWindow).Assembly.GetName().Version;
        var aboutMeta = _theme.Tertiary("Version " + (version is null ? "0.0" : version.ToString(3)) + " · one window for Hermes, Claude Code, Codex and OpenCode sessions");
        aboutMeta.TextWrapping = TextWrapping.Wrap; aboutMeta.TextTrimming = TextTrimming.None;
        aboutText.Children.Add(aboutMeta);
        Grid.SetColumn(aboutText, 1); about.Children.Add(aboutText);
        return about;
    }

    /// <summary>The keyboard reference, by what the keys act on, as the bars' side has the strip keys (Settings → Tabs). F1 shows it.</summary>
    internal (string Group, (string Keys, string Action)[] Shortcuts)[] KeyboardReference =>
    [
        ("Tabs",
        [
            ("Ctrl+Shift+T", "New session"), ("Ctrl+Shift+W", "Close tab"), ("Ctrl+Shift+D", "Duplicate tab (new session, same agent and folder)"),
            ("Ctrl+Tab", "Next tab (with Shift, the one before)"), ("Ctrl+1 … 9", "Jump to a tab (9, the last)"), ("Alt+1 … 9", "Jump to a tab, with the modifier the pane keys use"),
            ("Ctrl+F2", "Rename the tab"), ("F5", "Resume all tabs in this workspace (also Ctrl+Shift+R)"), ("F6", "Consolidate this workspace's tabs by folder")
        ]),
        ("Agent workspaces",
        [
            ("Ctrl+Shift+A", "List the agent workspaces to switch to (a click on the name at the head of the tab strip lists them too)"),
            ("Ctrl+Alt+1 … 9", "Switch to a workspace, in the Agent Workspaces panel's order (9, the last)"),
            ("Ctrl+Alt+Left / Right", "The previous or next workspace")
        ]),
        ("Panes",
        [
            ("F2", "Rename the focused pane; in a tab of one pane, or on the tab strip, the tab"),
            ("Alt+Shift+Plus", "Split right: a new session beside the focused pane"), ("Alt+Shift+Minus", "Split down: a new session below it (up to " + PaneLayout.MaxPanes + " panes per tab)"),
            ("Alt+Arrow", "Move to the pane on that side (where there is none, the key reaches the terminal)"),
            ("Ctrl+Shift+Z", "Zoom the focused pane over the others, and back; Alt+Arrow and a click on another pane move the zoom (Settings → Zoomed pane)"),
            TabsBottom
                ? ("Alt+Down", "From the bottom row, down to the tabs: Alt+Left and Alt+Right switch tabs, Alt+Up, Up or Enter go back in")
                : ("Alt+Up", "From the top row, up to the tabs: Alt+Left and Alt+Right switch tabs, Alt+Down, Down or Enter go back in"),
            ("Ctrl+Shift+E", "Open in File Explorer: the folder last clicked in Folders or Agent Workspaces, otherwise the focused pane's working folder")
        ]),
        ("Terminal", [("Ctrl+Shift+F", "Find in terminal"), ("Ctrl+C", "Copy when text is selected"), ("Ctrl+V, Ctrl+Shift+V or Shift+Insert", "Paste"), ("Ctrl+click", "Open a link")]),
        ("Window",
        [
            ("F1", "These keyboard shortcuts"), ("Ctrl+Shift+K", "Search conversations (outside a terminal, Ctrl+K too)"), ("Ctrl+Shift+S", "Show or hide the panel"),
            ("F11", "Full screen, and back: the rail and the panel wait at their edge for the pointer, or for Ctrl+Shift+K or Ctrl+Shift+S")
        ])
    ];

    /// <summary>The keyboard reference laid out: each group's name over its keys and what they do.</summary>
    private Grid BuildShortcutList()
    {
        var keys = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        keys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keys.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Add(FrameworkElement element, int column = 0, int span = 1)
        {
            Grid.SetRow(element, keys.RowDefinitions.Count - 1); Grid.SetColumn(element, column); Grid.SetColumnSpan(element, span);
            keys.Children.Add(element);
        }
        foreach (var (group, shortcuts) in KeyboardReference)
        {
            keys.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = _theme.BodyText(group, 12, 600); heading.Foreground = _theme.TextSecondary;
            heading.Margin = new Thickness(0, keys.RowDefinitions.Count == 1 ? 0 : 8, 0, 0);
            Add(heading, span: 2);
            foreach (var (chord, what) in shortcuts)
            {
                keys.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var keysText = _theme.MonoText(chord); keysText.Foreground = _theme.Text; keysText.VerticalAlignment = VerticalAlignment.Top;
                Add(keysText);
                var action = _theme.Tertiary(what); action.TextWrapping = TextWrapping.Wrap; action.TextTrimming = TextTrimming.None;
                Add(action, column: 1);
            }
        }
        return keys;
    }

    private TextBlock Section(string text)
    {
        var eyebrow = _theme.Eyebrow(text);
        eyebrow.Margin = new Thickness(0, 10, 0, 0);
        _displayText.Add(eyebrow);
        return eyebrow;
    }

    /// <summary>
    /// What a setting does, on its control rather than on the page: a Settings page is labels and controls, and the sentence
    /// behind each one is its tooltip, with the README as the full account (docs/decisions/settings-labels.md). Status that
    /// changes, such as the update state or a console host, stays on the page as a line of its own.
    /// </summary>
    private static void Explain(DependencyObject control, string? text) => ToolTipService.SetToolTip(control, text);

    /// <summary>Puts the drop list and the swatch on the saved theme, without applying it again.</summary>
    private void RefreshThemePicker()
    {
        if (_themes is null) return;
        var theme = ThemeCatalog.Find(_preferences.Theme);
        _themes.Select(theme.Id);
        _themeSwatch.Background = Theme.Brush(theme.Light ? theme.Base : theme.Terminal.Background);
        _themeSwatch.BorderBrush = Theme.Brush(theme.Light ? "00000026" : "FFFFFF33");
        _themeSwatchDot.Background = Theme.Brush(theme.Accent);
    }

    /// <summary>The folder the user's theme files are read from: themes, under SIC's data folder.</summary>
    internal string ThemesFolder => Path.Combine(_options.DataDirectory, "themes");

    /// <summary>Fills the drop list from the catalog as it is now: the built-in themes, then the user's files in file-name order.</summary>
    private void FillThemePicker() => _themes.Fill(ThemeCatalog.All.Select(theme => (theme.Id, theme.Name,
        theme.SystemAccent || theme.Mica ? "Windows accent color and Mica" : ThemeCatalog.IsBuiltIn(theme.Id) ? null : "From your Themes folder")));

    /// <summary>The theme files that could not be used, each with its error, under the theme buttons; nothing while every file reads.</summary>
    private void RefreshThemeProblems() => _themes?.ShowProblems(ThemeCatalog.Problems.Select(problem => (problem.File, problem.Error)).ToList());

    /// <summary>
    /// Settings → Reload: reads the themes folder again and lists what it holds. The theme chosen (or <paramref name="apply"/>) is applied again, so an edit
    /// to its file shows at once, and the default takes its place when its file is gone.
    /// </summary>
    internal void ReloadThemes(bool notify, string? apply = null)
    {
        var problems = ThemeCatalog.Reload(ThemesFolder);
        foreach (var problem in problems) Diagnostics.Log("theme file " + problem.File + ": " + problem.Error);
        FillThemePicker();
        RefreshThemeProblems();
        ApplyTheme(apply ?? _preferences.Theme);
        if (!notify) return;
        var own = ThemeCatalog.All.Count - ThemeCatalog.BuiltIn.Count;
        var listed = own == 1 ? "1 custom theme loaded" : own + " custom themes loaded";
        if (problems.Count == 0) NotifyBriefly("Themes", listed + ".");
        else Notify(InfoBarSeverity.Warning, "Themes", listed + ". Not loaded: " + string.Join(", ", problems.Select(problem => problem.File)) + ". Details are under the Theme buttons.");
    }

    /// <summary>Settings → Themes folder: opens it in File Explorer, making it first. The smoke records nothing and opens nothing.</summary>
    private void OpenThemesFolder()
    {
        try
        {
            Directory.CreateDirectory(ThemesFolder);
            if (!_options.SmokeTest) Paths.OpenInExplorer(ThemesFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowError("The themes folder could not be opened: " + exception.Message);
        }
    }

    /// <summary>
    /// Settings → Copy to a file: the chosen theme written to the themes folder under a new id and name, every value in it,
    /// listed and applied at once, and the folder opened beside it; edit the file and Reload.
    /// </summary>
    private void CopyThemeToFile()
    {
        var theme = _theme.Definition;
        try
        {
            Directory.CreateDirectory(ThemesFolder);
            var id = theme.Id + "-copy";
            var name = theme.Name + " Copy";
            for (var number = 2; ThemeCatalog.All.Any(item => item.Id == id) || File.Exists(Path.Combine(ThemesFolder, id + ThemeFiles.Extension)); number++)
            {
                id = theme.Id + "-copy-" + number;
                name = theme.Name + " Copy " + number;
            }
            var path = Path.Combine(ThemesFolder, id + ThemeFiles.Extension);
            File.WriteAllText(path, ThemeFiles.Write(theme with { Id = id, Name = name }));
            ReloadThemes(notify: false, apply: id);
            NotifyBriefly("Themes", Path.GetFileName(path) + " created and applied. Edits apply on Reload.");
            if (!_options.SmokeTest) Paths.OpenInExplorer(ThemesFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowError("The theme could not be written: " + exception.Message);
        }
    }

    /// <summary>Settings → Always exit cleanly. Saved at once; the next close of a tab, a pane or the app reads it.</summary>
    internal void SetAlwaysExitCleanly(bool on)
    {
        _preferences.AlwaysExitCleanly = on;
        SavePreferences();
    }

    /// <summary>Settings → Menu Animation. Saved at once; the next time the panel opens or shuts reads it.</summary>
    internal void SetMotion(string setting)
    {
        _preferences.Motion = MotionSetting.Normalize(setting);
        SavePreferences();
    }

    /// <summary>Settings → Side Panel. Saved and applied at once: the rail and its panel move to that side of the window.</summary>
    internal void SetSidebarSide(string side)
    {
        _preferences.Sidebar = SidebarSide.Normalize(side);
        SavePreferences();
        ApplySidebarSide();
    }

    /// <summary>
    /// Settings → Autohide sidebar. Saved at once; on, the rail goes once the panel is shut (at once when it already is), and off,
    /// it comes back to its column. Full screen hides it either way.
    /// </summary>
    internal void SetSidebarAutoHide(bool on)
    {
        _preferences.SidebarAutoHide = on;
        SavePreferences();
        if (on) AutoHideSidebar(animate: true);
        else DockSidebar();
    }

    /// <summary>
    /// Settings → Chrome. Saved and applied at once: frosted or clear, the window's backdrop becomes acrylic or nothing
    /// and the rail, the panel, the strip and the status bar are painted part-clear over it; solid, the theme's own colors and
    /// Mica come back.
    /// </summary>
    internal void SetChrome(string style)
    {
        style = ChromeStyle.Normalize(style);
        _preferences.Chrome = style;
        SavePreferences();
        _theme.Frosted = ChromeStyle.SeeThrough(style);
        _backdrop.Style = style;
        _theme.Apply(_theme.Definition);
        ApplyThemeChrome();
        PaintTabs();
        RefreshWorkspaceChip();
    }

    /// <summary>Settings → Tabs. Saved and applied at once: the tabs, the pane headers and the window buttons move to that edge, the running terminals with them as they are.</summary>
    internal void SetTabBarSide(string side)
    {
        _preferences.TabBar = TabBarSide.Normalize(side);
        SavePreferences();
        ApplyTabBarSide();
    }

    private Slider? _paneBorderSlider;
    private TextBlock? _paneBorderValue;

    /// <summary>
    /// Settings → Session Indicator Width. Saved and applied at once to every pane, the one with the keyboard showing it; no
    /// terminal is resized. The slider and the width beside it follow a change made from code.
    /// </summary>
    internal void SetPaneBorder(int width)
    {
        _preferences.PaneBorderWidth = PaneBorder.Clamp(width);
        SavePreferences();
        foreach (var pane in AllPanes) pane.RingWidth = _preferences.PaneBorderWidth;
        if (_paneBorderValue is not null) _paneBorderValue.Text = PaneBorder.Label(_preferences.PaneBorderWidth);
        if (_paneBorderSlider is { } slider && slider.Value != _preferences.PaneBorderWidth) slider.Value = _preferences.PaneBorderWidth;
    }

    private TextBlock? _paneZoomValue;

    /// <summary>Settings → Zoomed pane and its size. Saved, and every zoomed pane's card made again at once.</summary>
    internal void SetPaneZoom(string frame, int percent)
    {
        _preferences.PaneZoom = PaneZoom.Normalize(frame);
        _preferences.PaneZoomPercent = PaneZoom.Clamp(percent);
        SavePreferences();
        if (_paneZoomValue is not null) _paneZoomValue.Text = PaneZoom.PercentLabel(_preferences.PaneZoomPercent);
        RefreshZooms();
    }

    /// <summary>Settings → Close and minimize. Saved at once; the next close or minimize reads it.</summary>
    internal void SetCloseAndMinimize(string choice)
    {
        _preferences.CloseAndMinimize = CloseAndMinimize.Normalize(choice);
        SavePreferences();
    }

    /// <summary>Settings → Sound, None or a sound: whether a turn that ends rings. Saved at once; the next turn that ends reads it.</summary>
    internal void SetReadyChime(bool on)
    {
        _preferences.ReadyChime = on;
        SavePreferences();
        RefreshSoundPicker();
    }

    /// <summary>Settings → Resume sessions on launch. Saved at once; the next launch reads it.</summary>
    internal void SetResumeAllOnLaunch(bool on)
    {
        _preferences.ResumeAllOnLaunch = on;
        SavePreferences();
    }

    private ToggleSwitch? _confirmPasteSwitch;

    /// <summary>
    /// Settings → Terminal → Confirm multi-line pastes into PowerShell, or the paste question's Don't ask again, which the switch
    /// then shows. Saved at once; the next paste reads it.
    /// </summary>
    internal void SetConfirmMultiLinePaste(bool on)
    {
        _preferences.ConfirmMultiLinePaste = on;
        SavePreferences();
        if (_confirmPasteSwitch is { } toggle && toggle.IsOn != on) toggle.IsOn = on;
    }

    /// <summary>Changes a terminal option, keeps it within its bounds, saves it, and applies it to every live terminal.</summary>
    internal async Task ApplyTerminalOptionsAsync(Action<AppPreferences> change)
    {
        change(_preferences);
        _preferences.CursorStyle = TerminalOptions.NormalizeCursorStyle(_preferences.CursorStyle);
        _preferences.Scrollback = TerminalOptions.ClampScrollback(_preferences.Scrollback);
        SavePreferences();
        await ApplyAppearanceToAllAsync();
    }

    private async Task ApplyFontAsync(string family, int size)
    {
        family = family.Trim();
        if (family.Length is 0 or > 128 || size is < 8 or > 48) { ShowError("The font must have a name and a size from 8 to 48."); return; }
        _preferences.FontFamily = family; _preferences.FontSize = size;
        SavePreferences();
        await ApplyAppearanceToAllAsync();
    }

    /// <summary>
    /// Applies the terminal settings to every pane open now; a tab opened or closed meanwhile does not disturb the loop. The
    /// Settings controls call this from async void handlers, where an exception would end SIC and every agent with it, so a
    /// pane that fails is reported and the rest still get the change.
    /// </summary>
    private async Task ApplyAppearanceToAllAsync()
    {
        foreach (var pane in AllPanes.ToArray())
        {
            try { await pane.ApplyAppearanceAsync(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { ShowError("A terminal did not take the new settings: " + exception.Message); }
        }
    }
}
