using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Sic.Core.Alerts;
using Sic.Core.State;

namespace Sic.App;

/// <summary>
/// Completion sounds (docs/decisions/completion-sounds.md): the built-in recipes and the user's own WAV files from the sounds
/// folder, chosen under Settings → Alerts, and one per workspace home folder from Sound… on the workspace's menu (on the folder
/// card until November 8, 2026), which rings for a turn that ends in a pane working in that folder.
/// </summary>
internal sealed partial class MainWindow
{
    private const string PlayGlyph = "", SpeakerGlyph = "";
    /// <summary>The Sound drop list with its items by id and, under the Sound buttons, the sound files that could not be used.</summary>
    private CatalogPicker _sounds = null!;
    private ComboBox _soundPicker => _sounds.Box;
    private TextBlock _soundProblems => _sounds.Problems;
    /// <summary>The folder sound flyout, built once, and the folder it is open for.</summary>
    private Flyout? _folderSoundFlyout;
    private ComboBox _folderSoundPicker = null!;
    private TextBlock _folderSoundTitle = null!;
    private string? _soundFolder;
    private bool _syncingFolderSound;

    /// <summary>The folder the user's sound files are read from: sounds, under SIC's data folder.</summary>
    internal string SoundsFolder => Path.Combine(_options.DataDirectory, "sounds");

    /// <summary>The sound for a turn that ended in a working folder: the nearest workspace home's own, or the one chosen in Settings → Alerts.</summary>
    internal Sound SoundFor(string? workingDirectory) =>
        SoundCatalog.Find(FolderSounds.For(Folders, _preferences.FolderSound, workingDirectory) ?? _preferences.Sound);

    /// <summary>A folder's own sound, or the one chosen in Settings → Alerts: what the flyout's Play plays.</summary>
    private Sound SoundOf(string folder) => SoundCatalog.Find(_preferences.FolderSound.GetValueOrDefault(PinnedFolders.Normalize(folder)) ?? _preferences.Sound);

    /// <summary>The Sound list's first entry, which turns the ready chime off (AppPreferences.ReadyChime); no sound has an empty id.</summary>
    private const string NoSound = "";
    private Button _playSound = null!;

    /// <summary>
    /// Settings → Alerts → Sound: the drop list, None first, which is the chime off; Play, the Sounds folder and Reload buttons;
    /// and the problems line.
    /// </summary>
    private void BuildSoundSettings(StackPanel stack)
    {
        _sounds = new CatalogPicker(_theme, "Sound", StringComparer.OrdinalIgnoreCase);
        FillSoundPicker();
        _sounds.Box.SelectionChanged += (_, _) =>
        {
            if (_sounds.Syncing || _sounds.SelectedId is not { } id) return;
            if (id == NoSound) { if (_preferences.ReadyChime) SetReadyChime(false); return; }
            if (!_preferences.ReadyChime) SetReadyChime(true);
            if (!Sound.SameId(_preferences.Sound, id)) SetSound(id);
        };
        Explain(_sounds.Box, "Plays when an agent is ready");
        stack.Children.Add(_sounds.Box);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var play = _playSound = Theme.IconTextButton(PlayGlyph, "Play");
        play.Click += (_, _) => PlaySound(SoundCatalog.Find(_preferences.Sound));
        var folder = Theme.IconTextButton("", "Sounds");
        folder.Click += (_, _) => OpenSoundsFolder();
        var reload = Theme.IconTextButton("", "Reload");
        reload.Click += (_, _) => ReloadSounds(notify: true);
        tools.Children.Add(play); tools.Children.Add(folder); tools.Children.Add(reload);
        stack.Children.Add(tools);
        RefreshSoundPicker();
        stack.Children.Add(_sounds.Problems);
        RefreshSoundProblems();
    }

    private void PlaySound(Sound sound)
    {
        if (!ChimeSound.Play(sound)) ShowError("The sound did not play: no audio device is available.");
    }

    /// <summary>What the lists call a sound: its name, and for a file cut to the longest length, that it is.</summary>
    private static string SoundLabel(Sound sound) => sound.Cut ? sound.Name + "  (cut to " + SoundCatalog.MaxSeconds + " s)" : sound.Name;

    /// <summary>Fills the drop list from the catalog as it is now: None, the built-in sounds, then the user's files in file-name order.</summary>
    private void FillSoundPicker() => _sounds.Fill(SoundCatalog.All.Select(sound => (sound.Id, SoundLabel(sound), sound.BuiltIn ? null : "From your Sounds folder"))
        .Prepend((NoSound, "None", (string?)null)));

    /// <summary>Puts the drop list on None while the chime is off, else on the saved sound, or the chime when that sound is gone; Play is off with None.</summary>
    private void RefreshSoundPicker()
    {
        if (_sounds is null) return;
        _sounds.Select(_preferences.ReadyChime ? SoundCatalog.Find(_preferences.Sound).Id : NoSound);
        _playSound.IsEnabled = _preferences.ReadyChime;
    }

    /// <summary>The sound files that could not be used, each with its error, under the Sound buttons; nothing while every file reads.</summary>
    private void RefreshSoundProblems() => _sounds?.ShowProblems(SoundCatalog.Problems.Select(problem => (problem.File, problem.Error)).ToList());

    /// <summary>Settings → Sound: saved at once; the next turn that ends rings it.</summary>
    internal void SetSound(string id)
    {
        _preferences.Sound = SoundCatalog.Normalize(id);
        SavePreferences();
        RefreshSoundPicker();
    }

    /// <summary>Settings → Reload: reads the sounds folder again and lists what it holds, in Settings and in the folder sound flyout.</summary>
    internal IReadOnlyList<SoundProblem> ReloadSounds(bool notify)
    {
        var problems = SoundCatalog.Reload(SoundsFolder);
        foreach (var problem in problems) Diagnostics.Log("sound file " + problem.File + ": " + problem.Error);
        FillSoundPicker();
        RefreshSoundPicker();
        FillFolderSoundPicker();
        RefreshSoundProblems();
        if (!notify) return problems;
        var own = SoundCatalog.All.Count - SoundCatalog.BuiltIn.Count;
        var listed = own == 1 ? "1 custom sound loaded" : own + " custom sounds loaded";
        if (problems.Count == 0) NotifyBriefly("Sounds", listed + ".");
        else Notify(InfoBarSeverity.Warning, "Sounds", listed + ". Not loaded: " + string.Join(", ", problems.Select(problem => problem.File)) + ". Details are under the Sound list in Settings.");
        return problems;
    }

    /// <summary>Settings → Sounds: opens the sounds folder in File Explorer, making it first. The smoke opens nothing.</summary>
    private void OpenSoundsFolder()
    {
        try
        {
            Directory.CreateDirectory(SoundsFolder);
            if (!_options.SmokeTest) Paths.OpenInExplorer(SoundsFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowError("The sounds folder could not be opened: " + exception.Message);
        }
    }

    /// <summary>A flyout at <paramref name="anchor"/> for the folder's sound: Default, then every sound, and Play; one flyout, shown for whichever workspace's home asked.</summary>
    private void ShowFolderSoundFlyout(FrameworkElement anchor, string folder)
    {
        _soundFolder = folder;
        if (_folderSoundFlyout is null)
        {
            var flyout = _folderSoundFlyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };
            var panel = new StackPanel { Spacing = 8 };
            _folderSoundTitle = _theme.BodyText("", 13, 600);
            panel.Children.Add(_folderSoundTitle);
            _folderSoundPicker = new ComboBox { Width = 220 };
            AutomationProperties.SetName(_folderSoundPicker, "Folder sound");
            FillFolderSoundPicker();
            _folderSoundPicker.SelectionChanged += (_, _) =>
            {
                if (_syncingFolderSound || _soundFolder is not { } chosen || _folderSoundPicker.SelectedItem is not ComboBoxItem item) return;
                SetFolderSound(chosen, item.Tag as string);
            };
            panel.Children.Add(_folderSoundPicker);
            var hint = _theme.Tertiary("Plays when an agent in this folder is ready. Default is the sound in Settings.");
            hint.TextWrapping = TextWrapping.Wrap; hint.TextTrimming = TextTrimming.None; hint.MaxWidth = 220;
            panel.Children.Add(hint);
            var play = Theme.TextButton("Play", "Hear this folder's sound");
            play.Click += (_, _) => { if (_soundFolder is { } chosen) PlaySound(SoundOf(chosen)); };
            panel.Children.Add(play);
            flyout.Content = panel;
            flyout.Opening += (_, _) => RefreshFolderSoundPicker();
        }
        _folderSoundTitle.Text = "Sound for " + Paths.Leaf(folder);
        _folderSoundFlyout.ShowAt(anchor);
    }

    /// <summary>Fills the flyout's list from the catalog as it is now: Default, then the built-in sounds, then the user's files.</summary>
    private void FillFolderSoundPicker()
    {
        if (_folderSoundPicker is null) return;
        _syncingFolderSound = true;
        try
        {
            _folderSoundPicker.Items.Clear();
            _folderSoundPicker.Items.Add(new ComboBoxItem { Content = "Default", Tag = null });
            foreach (var sound in SoundCatalog.All) _folderSoundPicker.Items.Add(new ComboBoxItem { Content = SoundLabel(sound), Tag = sound.Id });
        }
        finally { _syncingFolderSound = false; }
        RefreshFolderSoundPicker();
    }

    /// <summary>Puts the flyout's list on its folder's sound, or Default, which a sound since gone also shows as.</summary>
    private void RefreshFolderSoundPicker()
    {
        if (_folderSoundPicker is null) return;
        var id = _soundFolder is { } folder ? _preferences.FolderSound.GetValueOrDefault(PinnedFolders.Normalize(folder)) : null;
        _syncingFolderSound = true;
        try
        {
            _folderSoundPicker.SelectedItem = _folderSoundPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => id is null ? item.Tag is null : Sound.SameId(item.Tag as string, id))
                ?? _folderSoundPicker.Items[0];
        }
        finally { _syncingFolderSound = false; }
    }

    /// <summary>A folder's sound: saved. Null clears it, back to the default.</summary>
    internal void SetFolderSound(string folder, string? id)
    {
        _preferences.FolderSound = FolderSounds.With(_preferences.FolderSound, folder, id);
        SavePreferences();
    }
}
