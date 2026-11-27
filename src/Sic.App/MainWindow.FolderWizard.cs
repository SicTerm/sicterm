using Sic.Core.Appearance;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Sic.App;

/// <summary>
/// Add folders (November 27, 2026): one popup that lists folders to add to the Folders list, each with an optional color and
/// emoji, and adds the ones ticked when Add folders is clicked. Add folder… picks another in Windows' folder picker, as many
/// times as wanted. On a PC whose agents have history it opens with the folders used most already listed and ticked
/// (<see cref="AutoPins"/>), which a first run used to add by itself; the person now decides (Chris).
/// <para>
/// It is reached three ways, each by a click: the home screen's Folders card, there while the list is empty, once Get set up
/// has nothing to show; the Folders panel's Add folders; and the line New session shows while the list is empty.
/// </para>
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>One folder in the popup: ticked or not, and the color and emoji it will be given.</summary>
    private sealed class WizardFolder
    {
        internal required string Path { get; init; }
        internal required CheckBox Check { get; init; }
        internal required TextBox Emoji { get; init; }
        internal string? Color { get; set; }
        internal bool Suggested { get; init; }
        internal Action? Repaint { get; set; }
    }

    private List<WizardFolder>? _wizardFolders;
    private bool _wizardAccepted;

    /// <summary>For the smoke: ticks or unticks a row and gives it a color and an emoji, as its controls would.</summary>
    internal void SetWizardRow(string path, bool ticked, string? color = null, string? emoji = null)
    {
        if (_wizardFolders?.FirstOrDefault(row => PinnedFolders.Same(row.Path, path)) is not { } row) return;
        row.Check.IsChecked = ticked; row.Color = color; row.Emoji.Text = emoji ?? ""; row.Repaint?.Invoke();
        RefreshWizard();
    }

    /// <summary>For the smoke: the popup closed as its Add folders button closes it.</summary>
    internal void AcceptFolderWizard() { _wizardAccepted = true; _folderWizard?.Hide(); }

    private StackPanel? _wizardList;
    private TextBlock? _wizardEmpty, _wizardMessage;
    private ContentDialog? _folderWizard;

    /// <summary>The popup while it is up, with its folders as (path, ticked, color, emoji); for verification.</summary>
    internal IReadOnlyList<(string Path, bool Ticked, string? Color, string? Emoji)>? FolderWizardRows =>
        _wizardFolders?.Select(row => (row.Path, row.Check.IsChecked == true, row.Color, FolderMarks.Last(row.Emoji.Text))).ToList();

    internal ContentDialog? FolderWizard => _folderWizard;

    /// <summary>Opens Add folders: the history's suggestions first, ticked, then whatever Add folder… adds.</summary>
    internal async Task ShowFolderWizardAsync()
    {
        // From New session, whose dialog is still closing: one dialog at a time.
        for (var wait = 0; _dialogsOpen > 0 && wait < 20; wait++) await Task.Delay(50);
        if (_dialogsOpen > 0 || _folderWizard is not null) return;
        _wizardAccepted = false;
        var content = new StackPanel { Spacing = 12, MinWidth = 500, MaxWidth = 600 };
        var intro = _theme.BodyText(FolderWizardText.Intro, 13); intro.TextWrapping = TextWrapping.Wrap; intro.Foreground = _theme.TextSecondary;
        content.Children.Add(intro);
        _wizardFolders = [];
        _wizardList = new StackPanel { Spacing = 2 };
        _wizardEmpty = _theme.Tertiary(FolderWizardText.Empty); _wizardEmpty.Margin = new Thickness(2, 6, 0, 6);
        _wizardList.Children.Add(_wizardEmpty);
        content.Children.Add(new ScrollViewer { Content = _wizardList, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var add = Theme.IconTextButton(NewFolderGlyph, "Add folder…");
        AutomationProperties.SetName(add, "Add folder");
        add.Click += async (_, _) => await PickWizardFolderAsync();
        content.Children.Add(add);
        _wizardMessage = _theme.Caption(""); _wizardMessage.TextWrapping = TextWrapping.Wrap; _wizardMessage.TextTrimming = TextTrimming.None; _wizardMessage.Visibility = Visibility.Collapsed;
        content.Children.Add(_wizardMessage);
        var dialog = _folderWizard = Dialog(FolderWizardText.Title, content, "Add folders");
        dialog.Resources["ContentDialogMaxWidth"] = 680.0;
        var suggestions = await WizardSuggestionsAsync();
        if (suggestions.Count > 0 && _wizardList is not null)
        {
            // Where they came from, so a list already filled in does not read as a mistake.
            var from = _theme.Eyebrow(FolderWizardText.Suggested); from.Margin = new Thickness(0, 0, 0, 2);
            _wizardList.Children.Add(from);
        }
        foreach (var folder in suggestions) AddWizardRow(folder, suggested: true);
        RefreshWizard();
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _folderWizard = null; }
        var rows = _wizardFolders;
        _wizardFolders = null; _wizardList = null; _wizardEmpty = null; _wizardMessage = null;
        if (result == ContentDialogResult.Primary || _wizardAccepted) ApplyFolderWizard(rows);
    }

    /// <summary>The folders the history says are used most, not listed and not offered before, checked off the UI thread; none when it cannot be read.</summary>
    private async Task<IReadOnlyList<string>> WizardSuggestionsAsync()
    {
        try
        {
            var now = DateTimeOffset.Now;
            var listed = Folders; var offered = _preferences.AutoPinnedFolders.ToList();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return await Task.Run(() => AutoPins.Choose(AutoPins.Rank(_catalog.FolderActivity(now - AutoPins.Window), now), listed, offered, home, Directory.Exists));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Diagnostics.Log("folder wizard suggestions: " + exception.Message); return []; }
    }

    private async Task PickWizardFolderAsync()
    {
        string? folder;
        try { folder = await PickFolderAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { SayInWizard("The folder picker did not open: " + exception.Message); return; }
        if (folder is null || _wizardFolders is null) return;
        if (IsFolder(folder)) { SayInWizard(Paths.Leaf(folder) + " is in Folders already."); return; }
        if (_wizardFolders.FirstOrDefault(row => PinnedFolders.Same(row.Path, folder)) is { } there) { there.Check.IsChecked = true; SayInWizard(Paths.Leaf(folder) + " is in the list already."); return; }
        AddWizardRow(folder, suggested: false);
        SayInWizard(null);
        RefreshWizard();
    }

    private void SayInWizard(string? text)
    {
        if (_wizardMessage is null) return;
        _wizardMessage.Text = text ?? "";
        _wizardMessage.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// A folder's row: its tick with the folder's name over its path, then its color (a swatch that opens the colors) and its
    /// emoji (a small box, with a button that opens Windows' emoji panel over it). For the smoke too, which adds folders without the picker.
    /// </summary>
    internal void AddWizardRow(string folder, bool suggested)
    {
        if (_wizardFolders is null || _wizardList is null) return;
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Spacing = 0 };
        label.Children.Add(_theme.BodyText(Paths.Leaf(folder), 13, 600));
        var path = _theme.Tertiary(folder); path.FontFamily = Theme.Mono; path.FontSize = 11;
        label.Children.Add(path);
        var check = new CheckBox { Content = label, IsChecked = true, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(check, Paths.Leaf(folder));
        check.Checked += (_, _) => RefreshWizard();
        check.Unchecked += (_, _) => RefreshWizard();
        grid.Children.Add(check);
        var emoji = new TextBox
        {
            Width = 52, MinWidth = 0, MaxLength = FolderMarks.MaxLength, PlaceholderText = "Emoji", TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, FontSize = 13
        };
        AutomationProperties.SetName(emoji, "Emoji for " + Paths.Leaf(folder));
        var row = new WizardFolder { Path = folder, Check = check, Emoji = emoji, Suggested = suggested };
        var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = _theme.StrokeStrong };
        var color = new Button { Content = swatch, Width = 32, Height = 32, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        void PaintSwatch()
        {
            var hex = ResolveColor(row.Color, _theme.StripDark);
            swatch.Background = hex is null ? _theme.Transparent : new SolidColorBrush(Theme.Parse(hex));
            ToolTipService.SetToolTip(color, hex is null ? "Color: none" : "Color");
            AutomationProperties.SetName(color, "Color for " + Paths.Leaf(folder) + (hex is null ? ", none" : ""));
        }
        PaintSwatch();
        row.Repaint = PaintSwatch;
        color.Click += (_, _) =>
        {
            var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };
            void Choose(string? value) { flyout.Hide(); row.Color = value; PaintSwatch(); }
            var stack = new StackPanel { Spacing = 8, Width = 4 * 32 + 3 * 8 };
            stack.Children.Add(_theme.Eyebrow("Color for " + Paths.Leaf(folder)));
            stack.Children.Add(SwatchGrid(TabColors.Presets.Select(preset => (preset.Name, ResolveColor(preset.Id, _theme.StripDark)!, (string?)preset.Id)), row.Color, Choose));
            AddThemeSwatches(stack, row.Color, Choose);
            stack.Children.Add(_theme.Divider());
            var none = FlyoutAction(((char)0xE711).ToString(), "No color");
            none.IsEnabled = row.Color is not null;
            none.Click += (_, _) => Choose(null);
            stack.Children.Add(none);
            flyout.Content = stack;
            flyout.ShowAt(color);
        };
        Grid.SetColumn(color, 1); grid.Children.Add(color);
        // One emoji kept: the last one typed or chosen, as the Folders panel's own emoji does.
        var keeping = false;
        emoji.TextChanged += (_, _) =>
        {
            if (keeping) return;
            var mark = FolderMarks.Last(emoji.Text);
            if ((mark ?? "") == emoji.Text) return;
            keeping = true;
            try { emoji.Text = mark ?? ""; emoji.SelectionStart = emoji.Text.Length; }
            finally { keeping = false; }
        };
        Grid.SetColumn(emoji, 2); grid.Children.Add(emoji);
        var pick = _theme.IconButton(((char)0xE76E).ToString(), "Choose an emoji", 32, 14);
        AutomationProperties.SetName(pick, "Choose an emoji for " + Paths.Leaf(folder));
        pick.VerticalAlignment = VerticalAlignment.Center;
        pick.Click += async (_, _) =>
        {
            emoji.Focus(FocusState.Programmatic);
            if (_options.SmokeTest) return;
            // The panel attaches to the control focused when the chord arrives; a moment lets the focus settle.
            await Task.Delay(80);
            if (!EmojiPanel.Open()) SayInWizard("The emoji panel did not open. Win + . opens it.");
        };
        Grid.SetColumn(pick, 3); grid.Children.Add(pick);
        _wizardFolders.Add(row);
        _wizardList.Children.Add(grid);
    }

    /// <summary>The empty line while no folder is listed, and Add folders only while one is ticked, its count on it.</summary>
    private void RefreshWizard()
    {
        if (_wizardFolders is null || _folderWizard is null) return;
        if (_wizardEmpty is not null) _wizardEmpty.Visibility = _wizardFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var ticked = _wizardFolders.Count(row => row.Check.IsChecked == true);
        _folderWizard.IsPrimaryButtonEnabled = ticked > 0;
        _folderWizard.PrimaryButtonText = ticked <= 1 ? "Add folder" + (ticked == 0 ? "s" : "") : $"Add {ticked} folders";
    }

    /// <summary>
    /// Adds the ticked folders in their order, each with its color and emoji, up to the list's limit, and says what was added.
    /// The history's suggestions shown are remembered as offered (<see cref="AppPreferences.AutoPinnedFolders"/>), ticked or not,
    /// so the next popup does not offer them again.
    /// </summary>
    internal void ApplyFolderWizard(IReadOnlyList<(string Path, bool Ticked, string? Color, string? Emoji, bool Suggested)> rows)
    {
        var suggested = rows.Where(row => row.Suggested).Select(row => row.Path).ToList();
        if (suggested.Count > 0) { _preferences.AutoPinnedFolders = PinnedFolders.Clean(_preferences.AutoPinnedFolders.Concat(suggested)); SavePreferences(); }
        var added = new List<string>();
        foreach (var row in rows.Where(row => row.Ticked))
        {
            if (IsFolder(row.Path)) continue;
            try { AddFolder(row.Path); }
            catch (InvalidOperationException exception) { ShowError(exception.Message); break; }
            if (row.Color is not null) SetFolderColor(row.Path, row.Color);
            if (row.Emoji is { Length: > 0 }) SetFolderEmoji(row.Path, row.Emoji);
            added.Add(row.Path);
        }
        Diagnostics.Log("folder wizard: added " + added.Count);
        RefreshFoldersCard();
        if (added.Count > 0) NotifyBriefly(added.Count == 1 ? "Added 1 folder" : $"Added {added.Count} folders", string.Join(", ", added.Select(Paths.Leaf)) + ".");
    }

    private void ApplyFolderWizard(List<WizardFolder>? rows)
    {
        if (rows is null) return;
        ApplyFolderWizard(rows.Select(row => (row.Path, row.Check.IsChecked == true, row.Color, FolderMarks.Last(row.Emoji.Text), row.Suggested)).ToList());
    }

    // ---- The home screen's Folders card ----

    private Border? _foldersHomeSlot;
    private Border? _foldersCard;

    /// <summary>Whether the home screen's Folders card is showing; for verification.</summary>
    internal bool FoldersCardShown => _foldersCard is { Visibility: Visibility.Visible };

    /// <summary>
    /// The Folders card, in Get set up's place once that has nothing to show (or was closed), while the list is empty and the card
    /// was not closed: one line on what folders are for, Add folders, and an X.
    /// </summary>
    private void RefreshFoldersCard()
    {
        var show = _setupReady && _foldersHomeSlot is not null && !_closing && _setupPanel is not { Visibility: Visibility.Visible } && Folders.Count == 0 && !_preferences.FoldersCardClosed;
        if (show && _foldersCard is null)
        {
            var content = new StackPanel { Spacing = 6 };
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(_theme.Heading(FolderWizardText.CardHeading, 16));
            var close = _theme.IconButton(((char)0xE711).ToString(), "Close", 28, 12);
            close.Click += (_, _) => { _preferences.FoldersCardClosed = true; SavePreferences(); RefreshFoldersCard(); };
            Grid.SetColumn(close, 1); top.Children.Add(close);
            content.Children.Add(top);
            var line = _theme.BodyText(FolderWizardText.CardLine, 13); line.TextWrapping = TextWrapping.Wrap; line.Foreground = _theme.TextSecondary;
            content.Children.Add(line);
            var add = _theme.PrimaryButton("Add folders");
            add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 6, 0, 0);
            add.Click += async (_, _) => await ShowFolderWizardAsync();
            content.Children.Add(add);
            _foldersCard = new Border
            {
                Child = content, Background = _theme.Layer, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20, 14, 16, 16), Margin = new Thickness(0, 16, 0, 4), HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _foldersHomeSlot!.Child = _foldersCard;
        }
        if (_foldersCard is not null) _foldersCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>The words of Add folders and the Folders card, in one place for review.</summary>
internal static class FolderWizardText
{
    internal const string Title = "Add folders";
    internal const string Intro = "Each folder becomes a workspace, where its tabs and conversations gather. A color and an emoji are optional.";
    internal const string Empty = "No folders listed yet. Add folder… picks one.";
    internal const string Suggested = "Used most in the agents' history";
    internal const string CardHeading = "Folders";
    internal const string CardLine = "Each folder listed becomes a workspace, where its tabs and conversations gather. More can be added at any time from the Folders panel.";
    internal const string NewSessionLine = "Folders keep sessions organized.";
}
