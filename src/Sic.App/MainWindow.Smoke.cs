using System.Text.Json;
using Sic.App.Controls;
using Sic.Core.Agents;
using Sic.Core.Shell;
using Sic.Core.Alerts;
using Sic.Core.Appearance;
using Sic.Core.Sessions;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Sic.App;

internal sealed partial class MainWindow
{
    private const string SmokeSessionId = "941efcc1-557b-4f8f-947a-b0fdb607d31c";
    /// <summary>A second fixture from three days earlier, so the grouped list has an Earlier group under Today; no query the smoke runs matches it.</summary>
    private const string SmokeOlderSessionId = "5d2c9a1e-3b7f-4c6a-9e8d-0f1a2b3c4d5e";
    /// <summary>Where stage 1 leaves the window and the restore stage expects it: on the primary display of any machine that runs the smoke.</summary>
    private static readonly WindowPlacement SmokePlacement = new(120, 90, 1280, 800, false);
    /// <summary>The panel width stage 1 leaves saved and the restore stage expects.</summary>
    private const int SmokePanelWidth = 400;
    /// <summary>The scrollback stage 1 leaves saved (with a block cursor, no blink, copy on select) and the restore stage expects.</summary>
    private const int SmokeScrollback = 2000;
    /// <summary>The renderer background every theme must leave alone.</summary>
    private static readonly string FixedTerminalBackground = "#" + ThemeCatalog.Terminal.Background.ToLowerInvariant();
    /// <summary>
    /// The fixture's pet: a 16×16 PNG of one colour, <see cref="SmokePetColor"/>, in base64, cut where the PET verb splits its
    /// transmission into two chunks (40 characters, then the rest). Synthetic, as every image the repository keeps.
    /// </summary>
    private const string SmokePetPngHead = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQ";
    private const string SmokePetPngTail = "kWg2AAAAFklEQVR42mP4sECBJMQwqmFUw/DVAAB52rAQuKcD2QAAAABJRU5ErkJggg==";
    private const string SmokePetColor = "#f0a020";
    /// <summary>The fixture pet's image ID; its placeholder cells are drawn in the colour that encodes it, 38;2;0;18;52.</summary>
    private const uint SmokePetId = 4660;
    private const string SmokeScript = "[Console]::WriteLine('READY'); while ($null -ne ($line = [Console]::ReadLine())) { " +
        "if ($line.StartsWith('PING:')) { [Console]::WriteLine('PONG:' + $line.Substring(5)) } " +
        "elseif ($line -eq 'SIZE') { [Console]::WriteLine(('COLS:{0};ROWS:{1}' -f [Console]::WindowWidth, [Console]::WindowHeight)) } " +
        "elseif ($line -eq 'BANNER') { 1..6 | ForEach-Object { [Console]::WriteLine(([char]27 + '[33m' + ('█' * 8) + [char]27 + '[0m')) } } " +
        // The banner after a pause longer than PresenceTracker.EchoWindow: work, where the banner at once is only an echo.
        "elseif ($line -eq 'WORK') { Start-Sleep -Milliseconds 500; 1..6 | ForEach-Object { [Console]::WriteLine(([char]27 + '[33m' + ('█' * 8) + [char]27 + '[0m')) } } " +
        // Two columns each, as the console host counts them: three emoji (🔌 📖 ⚡) and a symbol with VS16 (⚠️), then a marker. In
        // UTF-8, since the console's own code page (437) turns each of them into a question mark.
        "elseif ($line -eq 'WIDE') { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); [Console]::WriteLine((-join (0x1F50C, 0x1F4D6, 0x26A1 | ForEach-Object { [char]::ConvertFromUtf32($_) })) + [char]0x26A0 + [char]0xFE0F + '|WIDE') } " +
        // a with a combining acute (U+0301), then a marker, then the column the console host's cursor is on after them: the
        // packaged host, counting by grapheme cluster, says 7; Windows' own gave the accent a column and said 8.
        "elseif ($line -eq 'ZERO') { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); [Console]::Write('a' + [char]0x301 + 'b|ZERO'); [Console]::WriteLine(' HOST:' + [Console]::CursorLeft) } " +
        "elseif ($line -eq 'FILL') { 1..80 | ForEach-Object { [Console]::WriteLine('FILL:' + $_) } } " +
        // The row the console host's cursor is on, against the top of its window, and the rows it has, with the line's nonce.
        "elseif ($line.StartsWith('CURSOR:')) { [Console]::WriteLine(('ROW:{0};TOP:{1};ROWS:{2};{3}' -f [Console]::CursorTop, [Console]::WindowTop, [Console]::WindowHeight, $line.Substring(7))) } " +
        // Hermes's pet as its TUI sends it in kitty mode (docs/decisions/pet-images.md): the PNG as a kitty transmission for image
        // 4660 in a box of 4×2 cells, in two chunks (m=1, then m=0), then the 4×2 grid of placeholder cells (U+10EEEE, a row mark
        // on each row's first cell) in 4660's colour, two columns in, with a blank row above and below and the nonce on either
        // side. PETDEL deletes the image as Hermes does when its pet goes away; CLS clears the screen and the scrollback.
        "elseif ($line.StartsWith('PET:')) { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); $e = [string][char]27; $n = [string][char]13 + [char]10; " +
        "$p = [char]::ConvertFromUtf32(0x10EEEE); $c = $e + '[38;2;0;18;52m'; $t = $line.Substring(4); " +
        "[Console]::Write('PETBOX:' + $t + $n + $n + $e + '_Ga=T,U=1,i=4660,c=4,r=2,f=100,q=2,m=1;" + SmokePetPngHead + "' + $e + '\\' + $e + '_Gm=0;" + SmokePetPngTail + "' + $e + '\\' + " +
        "'  ' + $c + $p + [char]0x305 + ($p * 3) + $e + '[39m' + $n + '  ' + $c + $p + [char]0x30D + ($p * 3) + $e + '[39m' + $n + $n + 'PETEND:' + $t + $n) } " +
        "elseif ($line -eq 'PETDEL') { [Console]::Write([char]27 + '_Ga=d,d=i,i=4660,q=2' + [char]27 + '\\'); [Console]::WriteLine('PETDEL:DONE') } " +
        "elseif ($line -eq 'CLS') { [Console]::Write([char]27 + '[2J' + [char]27 + '[3J' + [char]27 + '[H') } " +
        "elseif ($line.StartsWith('TITLE:')) { [Console]::Title = [regex]::Unescape($line.Substring(6)) } " +
        // A Claude Code turn: its working title for the given milliseconds, then its idle one, with nothing typed in between.
        "elseif ($line.StartsWith('TURN:')) { [Console]::Title = ([string][char]0x25D0) + ' Fixture turn'; Start-Sleep -Milliseconds ([int]$line.Substring(5)); [Console]::Title = ([string][char]0x2733) + ' Fixture turn' } " +
        // An agent's status report (OSC 7501) with the given body, as Claude Code, Codex and OpenCode send theirs (docs/decisions/program-status.md).
        "elseif ($line.StartsWith('STATUS:')) { [Console]::Write([char]27 + ']7501;' + $line.Substring(7) + [char]27 + '\\') } " +
        "elseif ($line -eq 'EXIT') { break } }";
    private static readonly string[] SmokeShellArguments = ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", SmokeScript];
    /// <summary>What the smoke's own direct starts say asked for them.</summary>
    private static StartRequest SmokeStart => new("the verification run", DateTimeOffset.UtcNow);

    private async Task RunRestoreCheckAsync()
    {
        try
        {
            var restoredWithoutProcesses = _tabs.TabItems.Count >= 2 && AllPanes.All(pane => pane.Terminal is null && !pane.IsRunning)
                && AllPanes.Any(pane => pane.Descriptor.AgentId == "claude" && pane.Descriptor.SessionId == SmokeSessionId
                    && pane.Descriptor.WorkingDirectory == _options.DataDirectory);
            if (!restoredWithoutProcesses) throw new InvalidOperationException("A new application process did not restore exact inactive placeholders.");
            var themeRestored = _preferences.Theme == "fluent-light" && _theme.Definition.Id == "fluent-light" && _root.RequestedTheme == ElementTheme.Light;
            if (!themeRestored) throw new InvalidOperationException("The selected light appearance was not restored in the next application process.");
            var nameRestored = _preferences.ConversationNames.GetValueOrDefault("claude:" + SmokeSessionId) == "Blue Maple migration"
                && _open.Values.Any(host => host.Saved.CustomTitle == "Blue Maple migration" && host.Title.Text == "Blue Maple migration")
                && _catalog.Search("blue map", aliases: _preferences.ConversationNames).Any(item => item.SessionId == SmokeSessionId);
            if (!nameRestored) throw new InvalidOperationException("The custom name was not searchable and visible after restarting.");
            var collapsedRestored = !_preferences.PanelOpen && !_panelOpen && _panel.Width == 0 && _panel.Visibility == Visibility.Collapsed;
            if (!collapsedRestored) throw new InvalidOperationException("The collapsed panel layout was not restored.");
            var panelWidthRestored = _preferences.PanelWidth == SmokePanelWidth && _panelWidth == SmokePanelWidth && _panelBody.Width == SmokePanelWidth;
            // Opened without saving: the slide goes to the restored width, not the default.
            await SetPanelOpen(true, persist: false, animate: false);
            panelWidthRestored &= _panel.Width == SmokePanelWidth && _panelEdge.Visibility == Visibility.Visible;
            await SetPanelOpen(false, persist: false, animate: false);
            if (!panelWidthRestored) throw new InvalidOperationException($"The panel width was not restored: saved={_preferences.PanelWidth} slot={_panel.Width}.");
            var colorRestored = _preferences.ConversationColors.GetValueOrDefault("claude:" + SmokeSessionId) == "blue" && _preferences.RecentTabColors.SequenceEqual(["12AB34"])
                && _open.Values.Any(host => host.Panes.Any(pane => pane.Descriptor.SessionId == SmokeSessionId) && EffectiveColor(host.Saved) == "blue" && host.Paint.Bar.Visibility == Visibility.Visible)
                && _open.Values.Any(host => host.Saved.Color == "pink" && host.Paint.Bar.Visibility == Visibility.Visible);
            if (!colorRestored) throw new InvalidOperationException("Tab colors, the conversation color, or the recent custom color were not restored after restarting.");
            // The six-pane tree, with its ratios, and the four-pane tab saved as a grid before there were trees, now a tree of the
            // same shape. A tab is laid out only while it is showing, so the tree tab is selected for its tiling to be read.
            var treeTab = _open.Values.SingleOrDefault(host => host.Saved.Title == "PowerShell · tree");
            var treeShape = treeTab is { Panes.Count: 6, Saved.Layout: { } tree } && PaneLayout.Splits(tree).Count == 5 && tree.Ratio == 0.4 && tree.First?.Ratio == 0.65 && tree.Second?.Second?.Ratio == 0.3 && treeTab.Saved.KeepEven == false
                && treeTab.Saved.SplitRatio is null && PaneLayout.Leaves(tree).SequenceEqual(treeTab.Panes.Select(pane => pane.Descriptor.Id));
            var gridShape = _open.Values.Any(host => host.Panes.Count == 4 && host.Saved.Layout is { Ratio: 0.65, First.Ratio: 0.4, Second.Ratio: 0.4 } grid && PaneLayout.DirectionOf(grid) == PaneSplit.Rows && PanesTiled(host));
            _tabs.SelectedItem = treeTab?.Item;
            var treeTiled = treeTab is not null && await EventuallyAsync(() => treeTab.Content.ActualWidth > 0 && PanesTiled(treeTab));
            var treeRestored = treeShape && gridShape && treeTiled;
            if (!treeRestored) throw new InvalidOperationException($"The six-pane tree or the older four-pane grid did not come back tiled with its ratios after restarting: shape={treeShape} grid={gridShape} tiled={treeTiled}.");
            // A pane's own name comes back on its header, and the tab without a name of its own is still called after its first pane.
            var paneNameRestored = treeTab!.Saved.CustomTitle is null && treeTab.Title.Text == "Tree first" && treeTab.Panes[0].NameText == "Tree first"
                && treeTab.Panes[3].Descriptor.CustomTitle == "Tree fourth" && treeTab.Panes[3].NameText == "Tree fourth" && treeTab.Panes[1].NameText == "tree 2";
            if (!paneNameRestored) throw new InvalidOperationException($"Pane names did not come back after restarting: tab='{treeTab.Title.Text}' panes=[{string.Join(", ", treeTab.Panes.Select(pane => pane.NameText))}].");
            // Fitted to the display it lands on, which on any display that holds 1400×890 px of work area is exactly what stage 1 saved.
            var fitted = WindowPlacement.Fit(SmokePlacement, WorkAreaFor(SmokePlacement), DefaultPlacement);
            var placement = new WindowPlacement(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height,
                AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized });
            var windowPlacementRestored = _preferences.Window == SmokePlacement && placement == fitted;
            if (!windowPlacementRestored) throw new InvalidOperationException($"The window placement was not restored: saved={_preferences.Window} expected={fitted} actual={placement}.");
            // The second folder stage 1 listed, "kept" in the data folder, is a workspace with its one placeholder tab parked out of the
            // strip (regrouped by the rule on the read, Workspaces.Regroup); the first is current and the indicator shows.
            var keptFolder = PinnedFolders.Normalize(Path.Combine(_options.DataDirectory, "kept"));
            var workspacesRestored = _workspaces.Count == 2 && ReferenceEquals(_workspace, _workspaces[0]) && _workspaces[1].Home == keptFolder && _workspaces[1].Id == keptFolder && _workspaces[1].Parked.Count == 1
                && _open.TryGetValue(_workspaces[1].Parked[0], out var kept) && kept.Saved.Title == "PowerShell · kept" && !_tabs.TabItems.Contains(kept.Item)
                && kept.Panes[0].Terminal is null && _workspaceChip.Visibility == Visibility.Visible && WorkspaceName(_workspaces[1]) == "kept";
            if (!workspacesRestored) throw new InvalidOperationException($"The second folder's workspace did not come back parked with its tab after restarting: workspaces={_workspaces.Count} names=[{string.Join(", ", _workspaces.Select(WorkspaceName))}].");
            await SearchAsync();
            var pinRestored = _preferences.PinnedConversations.SetEquals(["claude:" + SmokeSessionId]) && SectionOf("claude:" + SmokeSessionId) == ConversationGroup.Pinned
                && _history.Items[0] is ListViewItem { Tag: ConversationSection { Group: ConversationGroup.Pinned } };
            if (!pinRestored) throw new InvalidOperationException("The pinned conversation was not listed under Pinned after restarting.");
            // The six-pane tab pinned whole in stage 1: its group is in the settings, the open tab is pinned and marked, and its row
            // leads the Pinned group with the dot that says the tab is open (docs/decisions/tab-groups.md).
            var treeGroupRestored = treeTab is not null && TabGroups.Find(_preferences.TabGroups, treeTab.Saved.Id) is { Panes.Count: 6 } && IsTabPinned(treeTab) && treeTab.Panes.All(pane => pane.PinShown)
                && _history.Items.OfType<ListViewItem>().FirstOrDefault(item => item.Tag is SavedTab group && group.Id == treeTab.Saved.Id) is { } treeGroupRow && _history.Items.IndexOf(treeGroupRow) == 1
                && _groupMarks.Any(mark => mark.Group.Id == treeTab.Saved.Id && mark.Slot.Visibility == Visibility.Visible);
            if (!treeGroupRestored) throw new InvalidOperationException("The pinned six-pane tab did not come back pinned and listed under Pinned after restarting.");
            // The Folders list comes back as stage 1 left it, the data folder (adopted from the seeded folder list) and the kept folder,
            // with the data folder's tree in the Folders panel, and the old lists are gone from the settings file (Workspaces.AdoptFolders).
            var foldersRestored = Folders.SequenceEqual([PinnedFolders.Normalize(_options.DataDirectory), keptFolder]) && _workspaces[0].Id == PinnedFolders.Normalize(_options.DataDirectory)
                && _preferences.PinnedFolders is null && _preferences.Workplaces is null && FolderTreeOf(_options.DataDirectory) is not null;
            if (!foldersRestored) throw new InvalidOperationException($"The Folders list did not come back after restarting: folders=[{string.Join("; ", Folders)}].");
            // The folder's emoji comes back: in the file and on the workspace's row, and on no tab or pane (they lost theirs on November 8, 2026).
            var folderEmojiRestored = _preferences.FolderEmoji.GetValueOrDefault(PinnedFolders.Normalize(_options.DataDirectory)) == "🍁"
                && _open.Values.All(host => host.Mark.Visibility == Visibility.Collapsed && host.Panes[0].FolderMarkText == "")
                && _workspaceRows[_workspaces[0].Id].Mark is { Visibility: Visibility.Visible, Text: "🍁" };
            if (!folderEmojiRestored) throw new InvalidOperationException($"The folder emoji did not come back after restarting, or came back on a tab: saved='{_preferences.FolderEmoji.GetValueOrDefault(PinnedFolders.Normalize(_options.DataDirectory))}'.");
            var terminalOptionsRestored = _preferences is { CursorStyle: "block", CursorBlink: false, Scrollback: SmokeScrollback, CopyOnSelect: true };
            if (!terminalOptionsRestored) throw new InvalidOperationException("The terminal options were not restored after restarting.");
            var alwaysExitCleanlyRestored = _preferences.AlwaysExitCleanly;
            if (!alwaysExitCleanlyRestored) throw new InvalidOperationException("Always exit cleanly was not restored after restarting.");
            // Restored on; and restoredWithoutProcesses above shows the restore-check run started nothing on its account.
            var resumeAllOnLaunchRestored = _preferences.ResumeAllOnLaunch && !ResumingAll;
            if (!resumeAllOnLaunchRestored) throw new InvalidOperationException("Resume all sessions on launch was not restored after restarting.");
            // The rail on the right from the first layout, the mark at the head of the tab strip (atop the rail still with the bars at
            // the bottom, where Windows draws no close button over it), and the chime still on.
            var sidebarRestored = _preferences.Sidebar == SidebarSide.Right && Grid.GetColumn(_rail) == 2 && Grid.GetColumn(_work) == 0 && _panelEdge.Reversed
                && (TabsBottom ? _railMark : _stripMark).Visibility == Visibility.Visible && (TabsBottom ? _stripMark : _railMark).Visibility == Visibility.Collapsed
                && _rail.TransformToVisual(_root).TransformPoint(default).X > _root.ActualWidth - RailWidth - 1.5;
            if (!sidebarRestored) throw new InvalidOperationException("The sidebar did not come back on the right after restarting.");
            // The bars at the bottom from the first layout: the strip in the template's lower row, under the panes, every tab turned
            // over, every pane's header under its terminal, Windows drawing no buttons and SIC's own at the strip's end.
            var restoredStrip = FindNamed<Grid>(_tabs, "TabContainerGrid");
            var tabBarRestored = _preferences.TabBar == TabBarSide.Bottom && _windowButtons.Visibility == Visibility.Visible
                && await EventuallyAsync(() => restoredStrip is not null && Grid.GetRow(restoredStrip) == 1 && StripHosts.All(TabTurned)
                    && restoredStrip.TransformToVisual(_tabs).TransformPoint(default).Y > _tabs.ActualHeight / 2 && AllPanes.All(item => item.HeaderAtBottom)
                    && AppWindow.TitleBar.PreferredHeightOption == Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed && AppWindow.TitleBar.RightInset == 0);
            if (!tabBarRestored) throw new InvalidOperationException($"The bars did not come back at the bottom after restarting: caption inset {AppWindow.TitleBar.RightInset} px, height option {AppWindow.TitleBar.PreferredHeightOption}.");
            // The widest pane border from the first layout, on every restored pane, and on the slider in Settings.
            var paneBorderRestored = _preferences.PaneBorderWidth == PaneBorder.Max && AllPanes.Any() && AllPanes.All(item => item.RingWidth == PaneBorder.Max)
                && _paneBorderSlider?.Value == PaneBorder.Max && _paneBorderValue?.Text == PaneBorder.Label(PaneBorder.Max)
                && _preferences.PaneBorderColor == "green" && AllPanes.All(item => item.RingColor == Theme.Parse("4ADE80"))
                && _paneBorderColorButton?.Content is Microsoft.UI.Xaml.Shapes.Ellipse { Fill: Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var restoredDot } } && restoredDot == Theme.Parse("4ADE80");
            if (!paneBorderRestored) throw new InvalidOperationException($"The pane border did not come back {PaneBorder.Max} px wide and green after restarting: {_preferences.PaneBorderWidth} px, slider {_paneBorderSlider?.Value}, color {_preferences.PaneBorderColor}.");
            var readyChimeRestored = _preferences.ReadyChime;
            if (!readyChimeRestored) throw new InvalidOperationException("The ready chime was not on after restarting.");
            // The sound chosen and the folder's own came back, with the sounds folder's file listed and its broken file named.
            var soundRestored = Sound.SameId(_preferences.Sound, "long-tone.wav") && _preferences.FolderSound.GetValueOrDefault(PinnedFolders.Normalize(_options.DataDirectory)) == "ding"
                && !SoundCatalog.Find("long-tone.wav").BuiltIn && SoundCatalog.Problems.Count == 1 && SoundFor(_options.DataDirectory).Id == "ding";
            if (!soundRestored) throw new InvalidOperationException($"The sounds did not come back after restarting: sound={_preferences.Sound} folder sounds={_preferences.FolderSound.Count} listed={SoundCatalog.All.Count} problems={SoundCatalog.Problems.Count}.");
            // Stage 1 left Pet images off, against its default.
            var petImagesRestored = !_preferences.PetImages;
            if (!petImagesRestored) throw new InvalidOperationException("Pet images was not off after restarting.");
            // Stage 1 opened the welcome on request, which marked it seen; a restart shows no film of its own.
            var welcomeSeenRestored = _preferences.WelcomeSeen == WelcomeVersion && _welcomeFilm is null && !WelcomeCardShown;
            if (!welcomeSeenRestored) throw new InvalidOperationException("The welcome was not marked seen after restarting, or showed itself: " + _preferences.WelcomeSeen);
            // The shipped icon, the only one since October 8, 2026: on the taskbar in the file for the taskbar's tone, with the
            // window under its shortcut's identity, whatever stage 1's settings file said (it saved a trial word, as an older copy might).
            var appIconRestored = _preferences.AppIcon == AppIcon.Default && _taskbarIcon != 0
                && string.Equals(_appIconPath, BrandFile(AppIcon.IconFor(AppIcon.Find(AppIcon.Default), ShellIcons.LightTaskbar()) + ".ico"), StringComparison.OrdinalIgnoreCase)
                && WindowIdentity.Get(WinRT.Interop.WindowNative.GetWindowHandle(this)) is null;
            if (!appIconRestored) throw new InvalidOperationException("The shipped app icon was not on the taskbar after restarting, or the window kept an identity of its own: " + _preferences.AppIcon + ", " + _appIconPath + ".");
            _options.WriteReport(new { success = true, restoredWithoutProcesses, workspacesRestored, themeRestored, nameRestored, collapsedRestored, panelWidthRestored, colorRestored, treeRestored, paneNameRestored, windowPlacementRestored, pinRestored, treeGroupRestored, foldersRestored, folderEmojiRestored, soundRestored, terminalOptionsRestored, alwaysExitCleanlyRestored, resumeAllOnLaunchRestored, sidebarRestored, tabBarRestored, paneBorderRestored, readyChimeRestored, petImagesRestored, appIconRestored, welcomeSeenRestored, windowPlacement = placement, windowPlacementFitted = fitted != SmokePlacement, restoredTabs = _tabs.TabItems.Count, restoredPanes = AllPanes.Count(), nativeTerminalsCreated = 0 });
        }
        catch (Exception exception) { Environment.ExitCode = 1; _options.WriteReport(new { success = false, restoredWithoutProcesses = false, error = exception.ToString() }); }
        finally { await CloseAsync(force: true); }
    }

    private void CreateSmokeHistory()
    {
        var definition = _preferences.Agents.Single(agent => agent.Id == "claude");
        var directory = Path.Combine(definition.HistoryDirectory, "projects", "verification");
        Directory.CreateDirectory(directory);
        var records = new object[]
        {
            new { type = "user", sessionId = SmokeSessionId, cwd = _options.DataDirectory, timestamp = DateTimeOffset.UtcNow.ToString("O"), message = new { role = "user", content = "Verification fixture: check the embedded terminal and exact saved identity." } },
            new { type = "assistant", sessionId = SmokeSessionId, cwd = _options.DataDirectory, timestamp = DateTimeOffset.UtcNow.ToString("O"), message = new { role = "assistant", content = "This is synthetic integration-test content, not a real agent response. The terminal on the next tab runs a real PowerShell console without calling a model." } },
            new { type = "custom-title", sessionId = SmokeSessionId, customTitle = "Fixture · terminal integration" }
        };
        File.WriteAllLines(Path.Combine(directory, SmokeSessionId + ".jsonl"), records.Select(record => JsonSerializer.Serialize(record)));
        var earlier = DateTimeOffset.UtcNow.AddDays(-3).ToString("O");
        var older = new object[]
        {
            new { type = "user", sessionId = SmokeOlderSessionId, cwd = _options.DataDirectory, timestamp = earlier, message = new { role = "user", content = "Older fixture: a request made three days before." } },
            new { type = "assistant", sessionId = SmokeOlderSessionId, cwd = _options.DataDirectory, timestamp = earlier, message = new { role = "assistant", content = "Older fixture reply, also made up for the smoke." } }
        };
        File.WriteAllLines(Path.Combine(directory, SmokeOlderSessionId + ".jsonl"), older.Select(record => JsonSerializer.Serialize(record)));
    }

    /// <summary>The pin button at the right of a conversation row.</summary>
    private static Button? RowPin(object item) =>
        item is ListViewItem { Content: StackPanel { Children: [Grid grid, ..] } } ? grid.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<Button>().LastOrDefault() : null;

    /// <summary>The folder emoji a row shows before its name, or null while it shows none.</summary>
    /// <summary>A WAV file for the sounds check: a 440 Hz tone at nine tenths of full scale, 16-bit mono at 22.05 kHz, this long.</summary>
    private static byte[] SmokeWav(double seconds)
    {
        const int rate = 22_050;
        var samples = new short[(int)(seconds * rate)];
        for (var index = 0; index < samples.Length; index++) samples[index] = (short)Math.Round(0.9 * 32767 * Math.Sin(2 * Math.PI * 440 * index / rate));
        return WavImage.Write(samples, rate, 1);
    }
    private static string? FolderMarkShown(TreeViewNode node) =>
        FolderRowView(node) is Grid grid && grid.Children.OfType<Grid>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault() is { Visibility: Visibility.Visible, Text: var mark } ? mark : null;

    /// <summary>The node has a container on screen and its row is the element that container presents.</summary>
    /// <summary>Whether a tab is drawn as <see cref="MirrorTab"/> leaves it for a strip at the bottom: its template's root turned over and its header turned back.</summary>
    private static bool TabTurned(TabHost host) => FindNamed<Grid>(host.Item, "LayoutRoot") is { RenderTransform: Microsoft.UI.Xaml.Media.ScaleTransform { ScaleY: -1 } }
        && host.Header.RenderTransform is Microsoft.UI.Xaml.Media.ScaleTransform { ScaleY: -1 };

    private static bool RowPresented(TreeView tree, TreeViewNode node) =>
        tree.ContainerFromNode(node) is TreeViewItem item && FolderRowView(node) is { } row && ReferenceEquals(Ancestor<TreeViewItem>(row), item);

    private static Conversation RowConversation(ListView list, int index) => (Conversation)((ListViewItem)list.Items[index]).Tag;

    /// <summary>The group label the conversation's row sits under in the list, or null when it is not listed or the list has no groups.</summary>
    /// <summary>The settings file as it is on disk, after any save still waiting its quiet period has been written.</summary>
    private AppPreferences SavedPreferences(Func<AppPreferences> createDefault) { FlushState(); return _preferencesStore.Load(createDefault); }

    /// <summary>The layout file as it is on disk, after any save still waiting its quiet period has been written.</summary>
    private WorkspaceLayout SavedLayout(Func<WorkspaceLayout> createDefault) { FlushState(); return _layoutStore.Load(createDefault); }

    private ConversationGroup? SectionOf(string key)
    {
        ConversationGroup? current = null;
        foreach (var item in _history.Items.OfType<ListViewItem>())
        {
            if (item.Tag is ConversationSection section) current = section.Group;
            else if (item.Tag is Conversation conversation && conversation.Key == key) return current;
        }
        return null;
    }

    private async Task RunSmokeTestAsync()
    {
        try
        {
            var data = _options.DataDirectory;
            // Always exit cleanly is on for a new install, which the smoke's fresh data folder is; the checks below take the close
            // question's path (answered Force under the smoke) until the setting's own check turns it on and leaves it on.
            _preferences.AlwaysExitCleanly = false;
            // Six rail buttons since November 8, 2026: Folders, Workspaces, Sessions, New session, Resume all and Settings (Consolidate tabs left
            // the rail that day for the workspace's menu; the light / dark toggle went on October 8). Four panels. The seeded folder list
            // became the Folders list (Workspaces.AdoptFolders), its one folder the one workspace, and the list is gone from the settings in
            // memory and on disk.
            var workspaceShell = _railItems.Count == 6 && _panels.Count == 4 && _workspaces.Count == 1 && ReferenceEquals(_workspace, _workspaces[0]) && PinnedFolders.Same(_workspace.Home, data)
                && Folders.SequenceEqual([PinnedFolders.Normalize(data)]) && _preferences.Workplaces is null && SavedPreferences(() => new AppPreferences()).Workplaces is null && WorkspaceName(_workspace) == Paths.Leaf(data)
                && !_railItems.ContainsKey("consolidate") && _panelOpen && _panel.Width == PanelLayout.DefaultWidth && _panelWidth == PanelLayout.DefaultWidth && _tabs.TabStripFooter is not null && _tabs.IsAddTabButtonVisible;
            if (!workspaceShell) throw new InvalidOperationException($"The rail, panel and tab shell were not populated, or the seeded folder did not become the Folders list: rail={_railItems.Count} panels={_panels.Count} folders=[{string.Join("; ", Folders)}].");
            // The agents' tiles (docs/decisions/agent-icons.md): the only agent's logo published is OpenCode's, beside SIC's letter
            // tiles for File Explorer's menu; detection has run, and each tile is the tool's own icon where one was found, read into the data
            // folder's cache at 32 and 48 px, or the agent's letter on its hue where none was. Claude Code found as an exe is its icon.
            var iconRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "icons");
            static int PngWidth(string path) { var bytes = File.ReadAllBytes(path); return bytes.Length > 24 ? bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19] : 0; }
            var tileIds = new[] { "hermes", "claude", "codex", "opencode" };
            var agentIcons = Directory.EnumerateFiles(iconRoot, "*.png").Select(Path.GetFileName).SequenceEqual([AgentIcons.OpenCodeLogo]) && tileIds.All(id =>
                File.Exists(Path.Combine(iconRoot, id + ".ico")) && _agentIconSources.ContainsKey(id) && ShowsAgentIcon(_theme.AgentTile(id), id, 24)
                && (_agentIconSources[id] is null || AgentIconCache.Frames.All(frame => _theme.AgentIcons?.FramePath(id, frame == 32 ? 16 : 48) is { } cached && PngWidth(cached) == frame)));
            var claudeExe = _detection.GetValueOrDefault("claude")?.Path is { } claudePath && claudePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? claudePath : null;
            // Its frames are the ones drawn for 32 and 48 px in the exe's fullest icon group, not the shell's 256 px image shrunk.
            agentIcons &= claudeExe is null || string.Equals(_agentIconSources["claude"]?.Path, claudeExe, StringComparison.OrdinalIgnoreCase)
                && AgentIconCache.Frames.All(frame => ExeIcons.Read(claudeExe, frame) is { Length: var length } && length == frame * frame * 4);
            // With the Codex desktop app installed from the Store, Codex's tile is its white knot on black, or failing that its logo on
            // the tile colour its manifest names. OpenCode's is the logo SIC ships, on its light tile.
            var codexApp = await Task.Run(() => StoreAppLogos.Find(AgentIcons.CodexPackage));
            var codexExpected = codexApp is null ? null : codexApp.Unplated is { } unplated ? new AgentIconSource(unplated, AgentIcons.CodexPlate) : new AgentIconSource(codexApp.Logo, AgentIcons.PlateColor(codexApp.BackgroundColor));
            agentIcons &= codexExpected is null
                ? _agentIconSources["codex"] is null
                : _agentIconSources["codex"] is { } codexSource && string.Equals(codexSource.Path, codexExpected.Path, StringComparison.OrdinalIgnoreCase)
                    && codexSource.Plate == codexExpected.Plate && _theme.AgentTile("codex", 16).Background is not null == (codexSource.Plate is not null);
            agentIcons &= _agentIconSources["opencode"] is { Plate: AgentIcons.OpenCodePlate } openCodeSource
                && string.Equals(openCodeSource.Path, Path.Combine(iconRoot, AgentIcons.OpenCodeLogo), StringComparison.OrdinalIgnoreCase);
            if (!agentIcons) throw new InvalidOperationException("An agent's tile is not the tool's own icon where one was found, or its letter where none was, or a logo is still published: "
                + string.Join(", ", tileIds.Select(id => id + "=" + (_agentIconSources.GetValueOrDefault(id) is { } found ? found.Path + (found.Plate is null ? "" : " on #" + found.Plate) : "letter"))) + ".");
            // PowerShell's tile is the terminal window SIC draws for it (Theme.TerminalArt) at every size a tile is shown: the dark
            // window, then its title bar, its prompt and its rim.
            static bool Terminal(Border tile) => tile is { Background: Microsoft.UI.Xaml.Media.LinearGradientBrush, Child: Grid { Children.Count: 3 } art }
                && art.Children[0] is Border { Height: > 0 } && art.Children[1] is Microsoft.UI.Xaml.Shapes.Path { StrokeThickness: >= 1.75 } && art.Children[2] is Border { BorderThickness.Top: 1 };
            if (!new[] { 14.0, 16, 24, 28, 40 }.All(size => Terminal(_theme.AgentTile("shell", size))))
                throw new InvalidOperationException("PowerShell's tile is not the terminal window SIC draws for it.");
            // The brand: the .ico ships every frame the shell asks for from 100% to 200% scaling (16 to 48 px each drawn on its own
            // pixel grid, 56 px up from the mark on the 256 grid), and so does the light-taskbar set; the tray loaded SIC's own
            // icon at the taskbar's small-icon size, which the .ico carries exactly, the taskbar button and the title bar have their
            // own frames at their sizes (not the 32 px big icon Windows would shrink), the rail draws the .ico's frame for its size
            // on screen where there is one, in the set for the rail's tone, the welcome page draws the mark above 48 px as filled
            // outlines and gradients with no plate behind them, in the set for the page's tone, and the rail's Resume all shows
            // the one-colour mark, not the play glyph.
            static int[] IcoFrames(string name)
            {
                var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "brand", name));
                return Enumerable.Range(0, BitConverter.ToUInt16(bytes, 4)).Select(index => bytes[6 + 16 * index] is 0 ? 256 : bytes[6 + 16 * index]).ToArray();
            }
            var icoFrames = IcoFrames("sic.ico");
            var lightIcoFrames = IcoFrames("sic-light.ico");
            static bool FullMark(object? mark) => mark is Viewbox { Child: Canvas { Children.Count: > 0 } canvas } && SicMark.FrameSize(mark) == 0
                && canvas.Children.All(child => child is Microsoft.UI.Xaml.Shapes.Path { Fill: not null })
                && canvas.Children.OfType<Microsoft.UI.Xaml.Shapes.Path>().Any(path => path.Fill is Microsoft.UI.Xaml.Media.LinearGradientBrush);
            var railPixels = (int)Math.Round(24 * (_root.XamlRoot?.RasterizationScale ?? 1));
            var railMark = _rail.Children.OfType<Border>().FirstOrDefault()?.Child;
            var railFrame = SicMark.FrameSize(railMark);
            var railTone = SicMark.LightArt(railMark) == SicMark.IsLightSurface(_theme.StripSurface);
            var brandMark = icoFrames.SequenceEqual([16, 20, 24, 28, 30, 32, 36, 40, 42, 48, 56, 64, 256]) && lightIcoFrames.SequenceEqual(icoFrames)
                && _tray is { IconSize: > 0 } tray && icoFrames.Contains(tray.IconSize)
                && _taskbarIconSize == ShellIcons.TaskbarButtonSize() && _taskbarIcon != 0
                && _titleBarIconSize == ShellIcons.SmallIconSize(WinRT.Interop.WindowNative.GetWindowHandle(this)) && _titleBarIcon != 0
                && railFrame > 0 && (railFrame == railPixels || !icoFrames.Contains(railPixels)) && railTone
                && _welcome.Children[0] is StackPanel welcome && FullMark(welcome.Children[0]) && SicMark.LightArt(welcome.Children[0]) == SicMark.IsLightSurface(_theme.Base.Color)
                && _railItems["resume"].Button.Content is Grid resumeContent && resumeContent.Children.OfType<Viewbox>().Any() && !resumeContent.Children.OfType<FontIcon>().Any();
            if (!brandMark) throw new InvalidOperationException($"The mark, the tray, taskbar or title-bar icon, or the Resume all glyph was not in place: ico frames [{string.Join(", ", icoFrames)}], light-taskbar ico frames [{string.Join(", ", lightIcoFrames)}], tray icon {_tray?.IconSize ?? 0} px, taskbar icon {_taskbarIconSize} px, title-bar icon {_titleBarIconSize} px, rail mark frame {railFrame} px for {railPixels} px on screen, rail mark in its rail's set {railTone}.");

            // By date a search is the one ranked list, which the checks from here read by row; by workspace, the default, it has
            // sections, checked with the grouping below.
            await SetGroupingAsync(ConversationListing.ByDate);
            _search.Text = "synthetic integration";
            await SearchAsync();
            var historySearch = _history.Items.Count == 1 && RowConversation(_history, 0).SessionId == SmokeSessionId;
            if (!historySearch) throw new InvalidOperationException("The imported fixture was not found by full-text search in the UI.");
            _history.SelectedItem = _history.Items[0];
            await WaitForAsync(() => Task.FromResult(_previewCard.Visibility == Visibility.Visible && Ancestor<ListViewItem>(_previewCard) is { } previewRow && ReferenceEquals(previewRow, _history.SelectedItem) && !_previewExcerpt.Text.StartsWith("Loading", StringComparison.Ordinal)));
            var previewShown = _tabs.TabItems.Count == 0 && _previewExcerpt.Text.Contains("Verification fixture", StringComparison.Ordinal) && _selectedConversation?.SessionId == SmokeSessionId;
            if (!previewShown) throw new InvalidOperationException("Selecting a conversation did not preview it in place without opening a tab.");

            var conversation = _catalog.Get("claude:" + SmokeSessionId)!;
            var opened = await OpenConversationAsync(conversation) ?? throw new InvalidOperationException("The conversation did not open as a tab.");
            var renamedTab = (TabViewItem)_tabs.SelectedItem!;
            var host = _open[renamedTab];
            var renameAvailable = renamedTab.ContextFlyout is MenuFlyout menu && menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Rename tab")
                && menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Tab color…")
                && menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Pin tab") && menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Pin and close tab") && PinnableKeys(host).SequenceEqual(["claude:" + SmokeSessionId])
                && ShowsAgentIcon(host.TileHost.Child as Border, "claude", 16)
                && opened.Terminal is null && host.Title.ReadLocalValue(TextBlock.ForegroundProperty) == DependencyProperty.UnsetValue;
            if (!renameAvailable) throw new InvalidOperationException("The tab has no rename, color or Pin and close affordance, no agent icon, or a fixed title ink that ignores the tab state.");
            BeginRename(host);
            var editor = host.Editor ?? throw new InvalidOperationException("Renaming did not open the in-place editor.");
            await WaitForAsync(() => Task.FromResult(editor.FocusState != FocusState.Unfocused));
            var editorShown = host.Title.Visibility == Visibility.Collapsed && host.Header.Children.Contains(editor) && editor.Text == TabLabel(host.Saved);
            editor.Text = "Discarded name";
            await EndRenameAsync(host, commit: false, refocus: false);
            var renameCancelled = host.Editor is null && !host.Header.Children.Contains(editor) && host.Title.Visibility == Visibility.Visible && host.Saved.CustomTitle is null;
            BeginRename(host);
            host.Editor!.Text = "  Blue Maple migration  ";
            await EndRenameAsync(host, commit: true, refocus: true);
            var inlineRename = editorShown && renameCancelled && host.Saved.CustomTitle == "Blue Maple migration" && host.Title.Text == "Blue Maple migration"
                && _preferences.ConversationNames.GetValueOrDefault("claude:" + SmokeSessionId) == "Blue Maple migration";
            if (!inlineRename) throw new InvalidOperationException("The in-place rename did not open on the current name, cancel with Esc, and save a trimmed name.");
            await RefreshHistoryAsync();
            _search.Text = "blue map";
            await SearchAsync();
            var renamedSearch = _history.Items.Count == 1 && RowConversation(_history, 0).SessionId == SmokeSessionId && RowConversation(_history, 0).Title == "Blue Maple migration";
            if (!renamedSearch) throw new InvalidOperationException("The renamed tab did not appear under its local name after a history refresh.");
            await OpenConversationAsync(RowConversation(_history, 0));
            if (!ReferenceEquals(_tabs.SelectedItem, renamedTab)) throw new InvalidOperationException("Searching the local name did not return to the exact existing tab.");
            await WaitForAsync(() => Task.FromResult(host.Paint.Bar.Parent is Grid { Name: "TabContainer" }));
            SetTabColor(host, "12ab34");
            var customRemembered = host.Saved.Color == "12AB34" && _preferences.RecentTabColors.FirstOrDefault() == "12AB34";
            SetTabColor(host, "blue");
            var blue = Theme.Parse(TabColors.Resolve("blue", _theme.StripDark)!);
            var tabColored = customRemembered && host.Saved.Color == "blue" && _preferences.ConversationColors.GetValueOrDefault("claude:" + SmokeSessionId) == "blue"
                && host.Paint.Bar.Visibility == Visibility.Visible && host.Paint.Stripe.Color == blue && TabColorFollowsSelection(host)
                && _preferences.RecentTabColors.SequenceEqual(["12AB34"]);
            if (!tabColored) throw new InvalidOperationException("Coloring the tab did not paint its stripe and the active wash its pane headers share, left a wash on the inactive fill, did not store the color against the conversation, or did not remember only the custom color.");
            SaveLayout();
            var saved = SavedLayout(() => throw new InvalidOperationException("The persisted layout was not found."));
            _open.Clear(); _tabs.TabItems.Clear(); _activePane = null;
            RestoreLayout(saved);
            var placeholderRestored = _tabs.TabItems.Count == 1 && AllPanes.All(pane => pane.Terminal is null)
                && AllPanes.Single().Descriptor.SessionId == SmokeSessionId && AllPanes.Single().Descriptor.WorkingDirectory == data
                && SelectedHost!.Saved.CustomTitle == "Blue Maple migration" && SelectedHost.Saved.Color == "blue";
            if (!placeholderRestored) throw new InvalidOperationException("Restore did not preserve identity, working folder, name and color without launching a process.");
            // Closing a linked tab refreshes the history in the background, and that refresh rebuilds the list when it ends, cancelling
            // a search in flight; the stages below read the list right after their own searches, so the refresh is waited out first.
            var refreshesBeforeClose = HistoryRefreshes;
            await CloseTabAsync((TabViewItem)_tabs.TabItems[0]);
            await WaitForAsync(() => Task.FromResult(HistoryRefreshes > refreshesBeforeClose && !_refresh.Running));
            _search.Text = "blue map"; await SearchAsync();
            if (_history.Items.Count != 1) throw new InvalidOperationException("The local name disappeared after closing its tab.");
            await OpenConversationAsync(RowConversation(_history, 0));
            var reopenedNamed = SelectedHost!.Saved.CustomTitle == "Blue Maple migration" && SelectedHost.Title.Text == "Blue Maple migration";
            if (!reopenedNamed) throw new InvalidOperationException("The local name was lost when reopening the saved conversation from search.");
            var reopenedColored = EffectiveColor(SelectedHost.Saved) == "blue" && SelectedHost.Paint.Bar.Visibility == Visibility.Visible;
            if (!reopenedColored) throw new InvalidOperationException("The tab color was lost when reopening the saved conversation from search.");
            // F2 in a tab of one pane renames the tab, whose name is its pane's too.
            await HandleShortcutAsync("rename");
            var f2Rename = SelectedHost.Editor is not null;
            await EndRenameAsync(SelectedHost, commit: false, refocus: false);
            if (!f2Rename || SelectedHost.Editor is not null) throw new InvalidOperationException("F2 in a tab of one pane did not open and close the in-place rename on the tab.");
            _search.Text = ""; await SearchAsync();

            // Grouping and pinning. The fixture was written just now, so it is listed under Today; pinned, it moves to Pinned
            // and is saved; unpinned it goes back; a query lists matches without group labels. It is left pinned for the restore stage.
            var fixtureKey = "claude:" + SmokeSessionId;
            // The list is grouped by folder unless chosen otherwise (docs/decisions/sessions-groups.md); the date groups are checked first.
            await SetGroupingAsync(ConversationListing.ByDate);
            var headersInert = _history.Items.OfType<ListViewItem>().Where(item => item.Tag is ConversationSection).All(item => !item.IsTabStop);
            var listedToday = SectionOf(fixtureKey) == ConversationGroup.Today && headersInert;
            await TogglePinAsync(conversation);
            var pinnedFirst = SectionOf(fixtureKey) == ConversationGroup.Pinned && _history.Items[0] is ListViewItem { Tag: ConversationSection { Group: ConversationGroup.Pinned } }
                && SavedPreferences(() => new AppPreferences()).PinnedConversations.Contains(fixtureKey);
            await TogglePinAsync(conversation);
            var unpinned = SectionOf(fixtureKey) == ConversationGroup.Today && !_preferences.PinnedConversations.Contains(fixtureKey);
            _search.Text = "blue map"; await SearchAsync();
            var queryFlat = _history.Items.Count == 1 && RowConversation(_history, 0).Key == fixtureKey;
            _search.Text = ""; await TogglePinAsync(conversation);
            var sessionsGrouped = listedToday && pinnedFirst && unpinned && queryFlat && SectionOf(fixtureKey) == ConversationGroup.Pinned;
            if (!sessionsGrouped) throw new InvalidOperationException($"The conversation list was not grouped, or pinning did not move the row to Pinned and back: today={listedToday} pinned={pinnedFirst} unpinned={unpinned} queryFlat={queryFlat}.");
            // The groups are now Pinned (the fixture) and Earlier (the older fixture). The first label sits flush; every later one
            // carries a rule above it. The pinned row shows the drawn pin, kept; the other row the outline glyph, hidden until hovered.
            var headers = _history.Items.OfType<ListViewItem>().Where(item => item.Tag is ConversationSection).ToArray();
            var sectionsRuled = headers.Length == 2 && headers[0].Tag is ConversationSection { Group: ConversationGroup.Pinned } && headers[1].Tag is ConversationSection { Group: ConversationGroup.Earlier }
                && headers[0].Content is StackPanel { Children: [Border { Child: SectionHeader }] } && headers[1].Content is StackPanel { Children: [Border { Child: null }, Border { Child: SectionHeader }] }
                // The hover is the header line's alone (November 12, 2026): the list's own highlight is off for a header row.
                && headers.All(item => item.Resources.ContainsKey("ListViewItemBackgroundPointerOver"));
            if (!sectionsRuled) throw new InvalidOperationException("The group labels did not come as Pinned then Earlier with a rule above every label but the first: " + string.Join(", ", headers.Select(item => (item.Tag as ConversationSection)?.Key)) + ".");
            var pinMarkDrawn = _history.Items.Count == 4 && RowPin(_history.Items[1]) is { Content: Viewbox, Opacity: 1, IsHitTestVisible: true }
                && RowPin(_history.Items[3]) is { Content: FontIcon { Glyph: PinGlyph }, Opacity: 0, IsHitTestVisible: false };
            if (!pinMarkDrawn) throw new InvalidOperationException("The pinned row did not show the drawn pin, or the unpinned row did not keep the hidden outline glyph.");
            // By folder, the default: the two fixtures share the data folder, so the list is Pinned then one folder group named
            // after it, with the state line saying so; its header folds the rows away, saved by the section's key, and the fold
            // holds through a rebuild (the sort changed) until the header unfolds it. Then by date again for the stages after this.
            await SetGroupingAsync(ConversationListing.ByFolder);
            var olderKey = "claude:" + SmokeOlderSessionId;
            var folderKey = "folder:" + PinnedFolders.Normalize(data);
            var folderHeaders = _history.Items.OfType<ListViewItem>().Where(item => item.Tag is ConversationSection).ToArray();
            var byFolder = folderHeaders.Length == 2 && folderHeaders[0].Tag is ConversationSection { Group: ConversationGroup.Pinned }
                && folderHeaders[1].Tag is ConversationSection { Group: ConversationGroup.Folder, Key: var sectionKey } && sectionKey == folderKey
                && folderHeaders[1].Content is StackPanel { Children: [Border, Border { Child: SectionHeader { Folded: false, Label.Text: var folderLabel } }] } && folderLabel == Paths.Leaf(data).ToUpperInvariant()
                && SectionOf(olderKey) == ConversationGroup.Folder && _historyCount.Text.Contains("by workspace", StringComparison.Ordinal) && _history.Items.Count == 4;
            ToggleFold(folderKey);
            var folded = _history.Items[3] is ListViewItem { Visibility: Visibility.Collapsed } && _history.Items[2] is ListViewItem { Visibility: Visibility.Visible, Content: StackPanel { Children: [Border, Border { Child: SectionHeader { Folded: true } }] } }
                && SavedPreferences(() => new AppPreferences()).FoldedSections.Contains(folderKey);
            await SetSortAsync(ConversationListing.ByName);
            var foldHeld = _history.Items[3] is ListViewItem { Visibility: Visibility.Collapsed } && _historyCount.Text.Contains("by name", StringComparison.Ordinal);
            ToggleFold(folderKey);
            var unfolded = _history.Items[3] is ListViewItem { Visibility: Visibility.Visible } && !_preferences.FoldedSections.Contains(folderKey);
            // A search by workspace lists its match under a section header, open whatever the whole list has folded.
            _search.Text = "synthetic integration"; await SearchAsync();
            var searchSections = _history.Items.Count == 2 && _history.Items[0] is ListViewItem { Tag: ConversationSection } && _history.Items[1] is ListViewItem { Visibility: Visibility.Visible }
                && RowConversation(_history, 1).SessionId == SmokeSessionId;
            _search.Text = ""; await SearchAsync();
            await SetSortAsync(ConversationListing.Newest);
            await SetGroupingAsync(ConversationListing.ByDate);
            var sessionsByFolder = byFolder && folded && foldHeld && unfolded && searchSections && SectionOf(olderKey) == ConversationGroup.Earlier;
            if (!sessionsByFolder) throw new InvalidOperationException($"Grouping by folder did not list one folder group that folds and unfolds: byFolder={byFolder} folded={folded} held={foldHeld} unfolded={unfolded} searchSections={searchSections} headers=[{string.Join("; ", folderHeaders.Select(item => (item.Tag as ConversationSection)?.Key))}] state='{_historyCount.Text}'.");
            // The New session dialog's recent folders, gathered with the group labels among the rows: the fixture folder is listed and nothing is cast.
            var newSessionFoldersListed = RecentFolders().Contains(data, StringComparer.OrdinalIgnoreCase);
            if (!newSessionFoldersListed) throw new InvalidOperationException("The New session dialog's recent folders did not list the fixture folder from the grouped list.");

            // Pin and close tab, on the older fixture opened as a tab: the tab closes, its conversation joins Pinned and is saved,
            // a brief notice says so, and the close reads the history again in the background, after which the Refresh button
            // beside the filter says when. The pin is undone after, so the restore stage finds the fixture pinned alone.
            var pinCloseKey = "claude:" + SmokeOlderSessionId;
            var pinCloseConversation = _catalog.Get(pinCloseKey) ?? throw new InvalidOperationException("The older fixture is not in the catalog.");
            var selectedBeforePinClose = _tabs.SelectedItem;
            var pinnedInPlacePane = await OpenConversationAsync(pinCloseConversation) ?? throw new InvalidOperationException("The older fixture did not open as a tab.");
            var pinnedInPlaceHost = HostOf(pinnedInPlacePane)!;
            var pinCloseName = pinnedInPlacePane.DisplayName;
            // First the pins that keep the tab, from the tab and from the pane: Pin tab, right above Pin and close tab, puts the
            // conversation under Pinned with the tab still open and says so briefly, naming the pane; the same item (Unpin tab, as it
            // then reads) takes it out again; Pin pane and Unpin pane do the same from the pane, which reads its own pin state.
            var pinTabOffered = pinnedInPlaceHost.Item.ContextFlyout is MenuFlyout pinTabMenu && pinTabMenu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray() is var pinTexts
                && Array.IndexOf(pinTexts, "Pin tab") >= 0 && Array.IndexOf(pinTexts, "Pin tab") == Array.IndexOf(pinTexts, "Pin and close tab") - 1 && pinnedInPlacePane.CanPin && !pinnedInPlacePane.IsPinned;
            // Each pin lists Sessions again; a listing started meanwhile (the refresh that detection's agent switches start at launch)
            // supersedes the pin's own, and the rows follow it a moment later, so the row's group is waited for rather than read at once.
            await TogglePinTabAsync(pinnedInPlaceHost.Item);
            var pinTabNotice = LastNotice;
            var pinnedInPlace = _open.ContainsKey(pinnedInPlaceHost.Item) && _preferences.PinnedConversations.Contains(pinCloseKey) && await EventuallyAsync(() => SectionOf(pinCloseKey) == ConversationGroup.Pinned)
                && pinnedInPlacePane.IsPinned && pinTabNotice is { Brief: true, Title: "Pinned to the top of Sessions" } pinTabShown && pinTabShown.Message == pinCloseName;
            await TogglePinTabAsync(pinnedInPlaceHost.Item);
            var unpinTabNotice = LastNotice;
            var unpinnedInPlace = _open.ContainsKey(pinnedInPlaceHost.Item) && !_preferences.PinnedConversations.Contains(pinCloseKey) && await EventuallyAsync(() => SectionOf(pinCloseKey) == ConversationGroup.Earlier)
                && !pinnedInPlacePane.IsPinned && unpinTabNotice is { Brief: true, Title: "Unpinned" } unpinTabShown && unpinTabShown.Message == pinCloseName;
            await TogglePinPaneAsync(pinnedInPlacePane);
            var pinPaneNotice = LastNotice;
            var panePinned = _preferences.PinnedConversations.Contains(pinCloseKey) && await EventuallyAsync(() => SectionOf(pinCloseKey) == ConversationGroup.Pinned) && pinnedInPlacePane.IsPinned
                && pinPaneNotice is { Brief: true, Title: "Pinned to the top of Sessions" } pinPaneShown && pinPaneShown.Message == pinCloseName;
            var panePinnedSection = SectionOf(pinCloseKey);
            await TogglePinPaneAsync(pinnedInPlacePane);
            var paneUnpinned = !_preferences.PinnedConversations.Contains(pinCloseKey) && !pinnedInPlacePane.IsPinned && LastNotice is { Brief: true, Title: "Unpinned" };
            if (!pinTabOffered || !pinnedInPlace || !unpinnedInPlace || !panePinned || !paneUnpinned)
                throw new InvalidOperationException($"Pin tab and Pin pane did not pin the open tab's conversation in place and take it out again: offered={pinTabOffered} pinned={pinnedInPlace} unpinned={unpinnedInPlace} panePinned={panePinned} (listed under {panePinnedSection}, pane pinned {pinnedInPlacePane.IsPinned}, notice '{pinPaneNotice?.Title}') paneUnpinned={paneUnpinned} notice='{LastNotice?.Title}: {LastNotice?.Message}'.");
            // Pin and close pane closes that pane alone: beside a shell pane added to the tab, the fixture's pane goes and is pinned,
            // the shell pane and the tab stay, and the close reads the history again. A shell pane has nothing to pin, so it is not named.
            var pinSidePane = AddPane(pinnedInPlaceHost, new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · pin side", data, ""), pinnedInPlacePane, PaneSplit.Columns);
            var refreshesBeforePanePin = HistoryRefreshes;
            await PinAndClosePaneAsync(pinnedInPlaceHost, pinnedInPlacePane);
            await WaitForAsync(() => Task.FromResult(HistoryRefreshes > refreshesBeforePanePin && !_refresh.Running));
            // The list's rebuild after the pin and the refresh may still be on its way (SearchAsync is cancelled and begun again by
            // the refresh's own), so the Pinned group is waited for rather than read once.
            var paneCloseKeptTab = _open.ContainsKey(pinnedInPlaceHost.Item) && pinnedInPlaceHost.Panes.SequenceEqual([pinSidePane]) && HostOf(pinnedInPlacePane) is null;
            var paneClosePinned = _preferences.PinnedConversations.Contains(pinCloseKey) && await EventuallyAsync(() => SectionOf(pinCloseKey) == ConversationGroup.Pinned);
            var paneCloseNoticed = LastNotice is { Brief: true, Title: "Pinned to the top of Sessions" } panePinCloseNotice && panePinCloseNotice.Message == pinCloseName;
            var panePinnedAndClosed = paneCloseKeptTab && paneClosePinned && paneCloseNoticed;
            var paneCloseSection = SectionOf(pinCloseKey);
            await TogglePinAsync(pinCloseConversation);
            await CloseTabAsync(pinnedInPlaceHost.Item);
            if (!panePinnedAndClosed)
                throw new InvalidOperationException($"Pin and close pane did not close the pane alone and pin it: keptTab={paneCloseKeptTab} (panes={pinnedInPlaceHost.Panes.Count}) pinned={paneClosePinned} (listed under {paneCloseSection}) noticed={paneCloseNoticed} notice='{LastNotice?.Title}: {LastNotice?.Message}'.");
            // A split tab is pinned whole, as a group (docs/decisions/tab-groups.md): the fixture beside a Claude Code pane not linked
            // to a conversation. Pin tab keeps the tab and marks it, and pins no conversation on its own; a rename of the tab reaches
            // the group; Pin and close tab closes the tab and keeps the group, listed under Pinned with no dot; Open as tab brings the
            // tab back under the same id with both panes, its name and the dot; Unpin tab takes the group out.
            var groupPane = await OpenConversationAsync(pinCloseConversation) ?? throw new InvalidOperationException("The older fixture did not open as a tab a second time.");
            var groupHost = HostOf(groupPane)!;
            var groupId = groupHost.Saved.Id;
            AddPane(groupHost, new SavedPane(Guid.NewGuid().ToString("N"), "claude", "Claude Code · unlinked beside", data, ""), groupPane, PaneSplit.Columns);
            SavedTab? Group() => TabGroups.Find(_preferences.TabGroups, groupId);
            ListViewItem? GroupRow() => _history.Items.OfType<ListViewItem>().FirstOrDefault(item => item.Tag is SavedTab tag && tag.Id == groupId);
            bool GroupDotShown() => _groupMarks.Any(mark => mark.Group.Id == groupId && mark.Slot.Visibility == Visibility.Visible);
            var groupOffered = CanPinTab(groupHost) && !IsTabPinned(groupHost) && groupHost.Panes.All(pane => !pane.PinShown) && Group() is null && GroupRow() is null;
            await TogglePinTabAsync(groupHost.Item);
            // Pinned whole, every pane in the tab shows the filled pin, the unlinked one included, while no conversation is pinned on its own.
            var groupPinned = Group() is { Panes.Count: 2 } && IsTabPinned(groupHost) && groupHost.Panes.All(pane => pane.PinShown)
                && !_preferences.PinnedConversations.Contains(pinCloseKey) && GroupRow() is { } groupRowOpen && _history.Items.IndexOf(groupRowOpen) == 1
                && _history.Items[0] is ListViewItem { Tag: ConversationSection { Group: ConversationGroup.Pinned } } && GroupDotShown()
                && LastNotice is { Brief: true, Title: "Pinned to the top of Sessions" } groupNotice && groupNotice.Message == pinCloseName + " · 2 panes";
            BeginRename(groupHost);
            groupHost.Editor!.Text = "Pinned pair";
            await EndRenameAsync(groupHost, commit: true, refocus: false);
            var groupRenamed = Group() is { CustomTitle: "Pinned pair" } && groupHost.Saved.CustomTitle == "Pinned pair";
            var refreshesBeforeGroupClose = HistoryRefreshes;
            await PinAndCloseTabAsync(groupHost.Item);
            await WaitForAsync(() => Task.FromResult(HistoryRefreshes > refreshesBeforeGroupClose && !_refresh.Running));
            var groupClosed = !_open.ContainsKey(groupHost.Item) && Group() is { CustomTitle: "Pinned pair", Panes.Count: 2 } && GroupRow() is not null && !GroupDotShown()
                && !_preferences.PinnedConversations.Contains(pinCloseKey) && SavedPreferences(() => new AppPreferences()).TabGroups.Any(group => group.Id == groupId && group.Panes.Count == 2)
                && LastNotice is { Brief: true, Title: "Pinned to the top of Sessions", Message: "Pinned pair · 2 panes" };
            var reopened = Group() is { } closedGroup ? OpenGroup(closedGroup) : null;
            var groupReopened = reopened is { Panes.Count: 2, Saved: { CustomTitle: "Pinned pair" } } && reopened.Saved.Id == groupId && reopened.Title.Text == "Pinned pair"
                && reopened.Panes[0].Descriptor.SessionId == SmokeOlderSessionId && reopened.Panes[1].Descriptor.Title == "Claude Code · unlinked beside" && reopened.Panes[1].Descriptor.SessionId is null
                && ReferenceEquals(_tabs.SelectedItem, reopened.Item) && reopened.Panes.All(pane => pane.PinShown) && GroupDotShown();
            // The header's pin on a pane of the pinned tab unpins the tab, as Unpin tab does.
            await TogglePinPaneAsync(reopened!.Panes[0]);
            var groupUnpinned = Group() is null && reopened.Panes.All(pane => !pane.PinShown) && GroupRow() is null && _open.ContainsKey(reopened.Item)
                && LastNotice is { Brief: true, Title: "Unpinned", Message: "Pinned pair" };
            var refreshesBeforeGroupGone = HistoryRefreshes;
            await CloseTabAsync(reopened.Item);
            await WaitForAsync(() => Task.FromResult(HistoryRefreshes > refreshesBeforeGroupGone && !_refresh.Running));
            if (!groupOffered || !groupPinned || !groupRenamed || !groupClosed || !groupReopened || !groupUnpinned)
                throw new InvalidOperationException($"A split tab was not pinned whole as a group, renamed into it, closed with it kept, reopened from it and unpinned: offered={groupOffered} pinned={groupPinned} renamed={groupRenamed} closed={groupClosed} reopened={groupReopened} unpinned={groupUnpinned} notice='{LastNotice?.Title}: {LastNotice?.Message}'.");
            _notice.IsOpen = false;
            // Pin and close tab on the fixture alone, as before.
            var pinClosePane = await OpenConversationAsync(pinCloseConversation) ?? throw new InvalidOperationException("The older fixture did not open as a tab a third time.");
            var pinCloseHost = HostOf(pinClosePane)!;
            var pinCloseOffered = pinCloseHost.Item.ContextFlyout is MenuFlyout pinCloseMenu && pinCloseMenu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Pin and close tab")
                && PinnableKeys(pinCloseHost).SequenceEqual([pinCloseKey]);
            var refreshesBefore = HistoryRefreshes;
            await PinAndCloseTabAsync(pinCloseHost.Item);
            await WaitForAsync(() => Task.FromResult(HistoryRefreshes > refreshesBefore && !_refresh.Running));
            var refreshTip = ToolTipService.GetToolTip(_refreshButton) as string ?? "";
            var pinnedAndClosed = pinCloseOffered && !_open.ContainsKey(pinCloseHost.Item) && _preferences.PinnedConversations.Contains(pinCloseKey)
                && SectionOf(pinCloseKey) == ConversationGroup.Pinned && SavedPreferences(() => new AppPreferences()).PinnedConversations.Contains(pinCloseKey)
                && LastNotice is { } pinNotice && pinNotice.Brief && pinNotice.Title == "Pinned to the top of Sessions" && pinNotice.Message == pinCloseName
                && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(_refreshButton) == "Refresh history" && refreshTip.Contains("last refreshed ", StringComparison.Ordinal);
            if (!pinnedAndClosed)
                throw new InvalidOperationException($"Pin and close tab did not close the tab, pin its conversation under Pinned, read the history again and say so: offered={pinCloseOffered} open={_open.ContainsKey(pinCloseHost.Item)} pinned={_preferences.PinnedConversations.Contains(pinCloseKey)} section={SectionOf(pinCloseKey)} notice='{LastNotice?.Title}: {LastNotice?.Message}' tip='{refreshTip}'.");
            await TogglePinAsync(pinCloseConversation);
            _notice.IsOpen = false;
            _tabs.SelectedItem = selectedBeforePinClose;

            var descriptor = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · integration check", data, "");
            var shellHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), descriptor.Title, [descriptor]));
            var harnessNameDropped = shellHost.Title.Text == "integration check" && ToolTipService.GetToolTip(shellHost.Item) is string tip && tip.StartsWith(descriptor.Title, StringComparison.Ordinal);
            if (!harnessNameDropped) throw new InvalidOperationException("The tab repeated the harness name its agent tile already shows, or its tooltip lost the full title.");
            var pane = shellHost.Panes[0];
            await pane.StartAsync(CreateShellRequest(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", SmokeScript], data), SmokeStart);
            var terminal = pane.Terminal!;
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("READY"));
            var rendererLineHeight = await terminal.ReadLineHeightAsync();
            if (rendererLineHeight != 1) throw new InvalidOperationException("The terminal inserts spacing between adjacent block-art rows.");
            // Wide characters through the real pseudoconsole: the marker after four two-column characters must start at column 8,
            // where the console host put it, or every repaint of a row holding an emoji lands off by the difference.
            await terminal.SendFromRendererAsync("WIDE\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("|WIDE"));
            var wideColumn = await terminal.ColumnOfAsync("|WIDE");
            if (wideColumn != 8) throw new InvalidOperationException($"Emoji and VS16 symbols did not take two columns each in the renderer: the marker after four of them started at column {wideColumn}, not 8.");
            // Microsoft's packaged console host is under the pane, not Windows' own, the page knows it (xterm's windowsPty), and
            // Settings → About says so (docs/decisions/console-host.md).
            var windowsPty = await terminal.ReadWindowsPtyAsync();
            var consoleHostLine = _consoleHostText?.Text ?? "";
            var consoleHost = terminal.PseudoconsoleHost == ConsoleHost.Packaged && ConsoleHost.FallbackReason is null
                && windowsPty is ("conpty", >= 21376) && consoleHostLine.StartsWith("Console host: OpenConsole 1.", StringComparison.Ordinal);
            if (!consoleHost) throw new InvalidOperationException($"The pane did not run in the packaged console host: host={terminal.PseudoconsoleHost} fallback={ConsoleHost.FallbackReason} windowsPty={windowsPty} about='{consoleHostLine}'.");
            // A zero-width combining accent through the real pseudoconsole: a + U+0301 is one column in the renderer, so the b after
            // it starts at column 1, and the host's own cursor agrees (7 after a, the accent and b|ZERO). Windows' own host gave the
            // accent a column of its own.
            await terminal.SendFromRendererAsync("ZERO\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("|ZERO HOST:"));
            var zeroColumn = await terminal.ColumnOfAsync("b|ZERO");
            var zeroHost = System.Text.RegularExpressions.Regex.Match(await terminal.ReadScreenAsync(), @"\|ZERO HOST:(\d+)").Groups[1].Value;
            var zeroWidth = zeroColumn == 1 && zeroHost == "7";
            if (!zeroWidth) throw new InvalidOperationException($"A combining accent took a column of its own: the b after a + U+0301 started at column {zeroColumn}, not 1, and the host's cursor was at {zeroHost}, not 7.");
            var nonce = Guid.NewGuid().ToString("N");
            await terminal.SendFromRendererAsync("PING:" + nonce + "\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:" + nonce));
            var f2Taken = await terminal.PressKeyAsync("F2");
            await WaitForAsync(() => Task.FromResult(shellHost.Editor is not null));
            await EndRenameAsync(shellHost, commit: false, refocus: true);
            var f2FromTerminal = f2Taken && shellHost.Editor is null && shellHost.Saved.CustomTitle is null;
            if (!f2FromTerminal) throw new InvalidOperationException("F2 inside a live terminal did not reach the tab rename.");
            SetTabColor(shellHost, "pink");
            if (shellHost.Saved.Color != "pink" || _preferences.ConversationColors.ContainsValue("pink")) throw new InvalidOperationException("A tab without a saved conversation must keep its color in the layout only.");
            await terminal.SendFromRendererAsync("BANNER\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("████████"));
            var bannerImage = Path.Combine(data, "banner-spacing.png");
            await terminal.CaptureAsync(bannerImage);

            // The fonts Settings lists (docs/decisions/terminal-fonts.md): each, applied through Settings' code path, is the one the
            // terminal draws with once its faces have loaded, the page's own as well as Windows'. A picture of each is kept.
            var fontBefore = _preferences.FontFamily;
            await terminal.SendFromRendererAsync("PING:The quick brown fox 0O1Il {}[] @&%$ -> != == ╭─╮\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:The quick"));
            var fontWidths = new List<double>();
            for (var index = 0; index < TerminalOptions.FontFamilies.Count; index++)
            {
                var family = TerminalOptions.FontFamilies[index];
                await ApplyFontAsync(family, _preferences.FontSize);
                await WaitForAsync(async () => await terminal.ReadFontAsync() is { Loaded: true } shown && shown.Family.StartsWith(family, StringComparison.Ordinal));
                fontWidths.Add(Math.Round((await terminal.ReadFontAsync()).CellWidth, 2));
                await terminal.CaptureAsync(Path.Combine(data, $"font-{index}.png"));
            }
            await ApplyFontAsync(fontBefore, _preferences.FontSize);
            await WaitForAsync(async () => (await terminal.ReadFontAsync()).Family.StartsWith(fontBefore, StringComparison.Ordinal));

            var findMatched = await terminal.FindAsync("PONG:" + nonce);
            pane.OpenFind();
            var findBarOpened = pane.Find.IsOpen;
            pane.Find.Close();
            if (!findMatched || !findBarOpened || pane.Find.IsOpen) throw new InvalidOperationException("Find in terminal did not match known output or the find bar did not open and close.");
            // Resolves the folder without launching Explorer, which would leave a window behind on every smoke run.
            if (!pane.CanOpenFolder || pane.FolderToOpen != data) throw new InvalidOperationException("The pane header's folder button is disabled or does not resolve to the pane's working folder.");
            // Ctrl+Shift+E from inside the live terminal, through the page's chord table, goes to the same folder (recorded, not opened).
            LastExplorerFolder = null;
            var folderKeyTaken = await terminal.PressKeyAsync("E", ctrl: true, shift: true);
            var folderKeyOpened = await EventuallyAsync(() => LastExplorerFolder == data);
            if (!folderKeyTaken || !folderKeyOpened) throw new InvalidOperationException($"Ctrl+Shift+E in the terminal did not open the pane's working folder: taken={folderKeyTaken} folder='{LastExplorerFolder}'.");

            // Terminal options reach the live renderer and are saved. Copy on select copies once on release when it is on and not
            // at all when it is off (this puts the fixture's PONG line on the clipboard); out-of-range values are brought back in bounds.
            await ApplyTerminalOptionsAsync(preferences => { preferences.CursorStyle = "block"; preferences.CursorBlink = false; preferences.Scrollback = SmokeScrollback; preferences.CopyOnSelect = false; });
            var copiedWhenOff = await terminal.SelectAndReleaseAsync("PONG:" + nonce);
            await ApplyTerminalOptionsAsync(preferences => preferences.CopyOnSelect = true);
            var copiedWhenOn = await terminal.SelectAndReleaseAsync("PONG:" + nonce);
            await WaitForAsync(() => Task.FromResult(pane.LastCopied == "PONG:" + nonce));
            var copyOnSelect = copiedWhenOff == "" && copiedWhenOn == "PONG:" + nonce;
            var rendererOptions = await terminal.ReadOptionsAsync();
            var savedOptions = SavedPreferences(() => new AppPreferences());
            await ApplyTerminalOptionsAsync(preferences => { preferences.CursorStyle = "beam"; preferences.Scrollback = 999_999; });
            var clampedOptions = await terminal.ReadOptionsAsync();
            var terminalOptionsApplied = rendererOptions == ("block", "outline", false, SmokeScrollback, true) && savedOptions is { CursorStyle: "block", CursorBlink: false, Scrollback: SmokeScrollback, CopyOnSelect: true }
                && clampedOptions == ("bar", "bar", false, TerminalOptions.MaxScrollback, true);
            await ApplyTerminalOptionsAsync(preferences => { preferences.CursorStyle = "block"; preferences.Scrollback = SmokeScrollback; });
            if (!copyOnSelect) throw new InvalidOperationException($"Copy on select did not copy once on release when on, or copied when off: off='{copiedWhenOff}' on='{copiedWhenOn}'.");
            if (!terminalOptionsApplied) throw new InvalidOperationException($"The terminal options did not reach the renderer, were not saved, or were not kept in bounds: applied={rendererOptions} clamped={clampedOptions}.");

            // A program's own copy (OSC 52) lands on the clipboard once; the menu's Copy may take it until the next press of the left
            // button. One that follows no key or click in the pane is told of in a notice that stays, with the start of the text; in
            // a PowerShell pane one just after a click gets a brief notice. A read leaves the clipboard alone, and a copy larger than
            // one message is refused and reported. An agent's copy after a click gets no notice at all (ClipboardTextTests).
            static string Osc52(string text) => "\x1b]52;c;" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) + "\x07";
            var programText = "SIC program copy ✓ " + nonce;
            var askedText = "SIC asked copy " + nonce;
            var copies = new List<(string Text, TimeSpan? Since)>(); var refusedLength = -1;
            void Copied(SessionPane _, string text, TimeSpan? since) => copies.Add((text, since));
            void Refused(SessionPane _, int characters) => refusedLength = characters;
            pane.ProgramCopied += Copied; pane.ProgramCopyRefused += Refused;
            try
            {
                _notice.IsOpen = false; LastNotice = null;
                await terminal.ForgetGestureAsync();
                await terminal.RenderAsync(Osc52(programText));
                var programCopyLanded = await EventuallyAsync(() => pane.LastCopied == programText && copies.Count == 1);
                var unaskedHeld = copies is [(_, null)] && LastNotice is { Brief: false, Severity: InfoBarSeverity.Warning, Title: "A program changed the clipboard" } unasked
                    && unasked.Message.Contains("“" + programText + "”", StringComparison.Ordinal);
                var offeredAfterCopy = await terminal.ProgramCopiedAsync();
                await terminal.PressLeftAsync();
                var offeredAfterPress = await terminal.ProgramCopiedAsync();
                // Just after that press, as Claude Code copies on the release of a drag.
                await terminal.RenderAsync(Osc52(askedText));
                var askedLanded = await EventuallyAsync(() => pane.LastCopied == askedText && copies.Count == 2);
                var askedBrief = askedLanded && copies[1].Since is { } since && since <= ClipboardText.AskedForWithin
                    && LastNotice is { Brief: true, Title: "Copied to the clipboard" } asked && asked.Message.Contains("“" + askedText + "”", StringComparison.Ordinal);
                await terminal.RenderAsync("\x1b]52;c;?\x07");
                await terminal.RenderAsync("\x1b]52;c;" + new string('A', 2_100_000) + "\x07");
                var oversizeRefused = await EventuallyAsync(() => refusedLength > 1_000_000);
                if (!programCopyLanded || !unaskedHeld || !offeredAfterCopy || offeredAfterPress || !askedBrief || !oversizeRefused || pane.LastCopied != askedText || copies.Count != 2)
                    throw new InvalidOperationException($"A program's copy (OSC 52) was not taken or told of as it should be: landed={programCopyLanded} unaskedHeld={unaskedHeld} offered={offeredAfterCopy} afterPress={offeredAfterPress} askedBrief={askedBrief} refused={refusedLength} copies=[{string.Join(", ", copies.Select(copy => copy.Since?.TotalMilliseconds.ToString("0") ?? "none"))}] last='{pane.LastCopied}' notice='{LastNotice?.Title}: {LastNotice?.Message}'.");
                // Another program holding the clipboard open, as rdpclip does after every change in a Remote Desktop session: a
                // copy waits it out and lands, where it failed with CLIPBRD_E_CANT_OPEN before November 11, 2026.
                var heldText = "SIC held copy " + nonce;
                var holding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var holder = Task.Run(() =>
                {
                    var opened = SmokeOpenClipboard(0);
                    holding.TrySetResult(opened);
                    Thread.Sleep(50);
                    if (opened) SmokeCloseClipboard();
                });
                var heldOpen = await holding.Task;
                var heldTries = ClipboardWrite.SetText(heldText);
                await holder;
                var heldRead = (await Win32Clipboard.ReadAsync()).Text;
                if (!heldOpen || heldTries < 2 || heldRead != heldText)
                    throw new InvalidOperationException($"A copy did not wait out the clipboard held by another program: held={heldOpen} tries={heldTries} read='{heldRead}'.");
            }
            finally
            {
                pane.ProgramCopied -= Copied; pane.ProgramCopyRefused -= Refused;
                // The refusal's notice is held, and a notice takes room from the terminals the checks below measure.
                _notice.IsOpen = false;
            }

            // The cursor as drawn. With the focus elsewhere, as it is in Settings while a cursor is chosen there, the pane draws
            // the chosen shape without blinking (a block hollow) rather than one hollow box for every choice. With the focus in
            // the terminal it blinks when blink is on and holds still when it is off, with the page told that Windows asks for
            // reduced motion, whatever this machine's own setting: that is where the page's reduced-motion rule used to stop
            // the blink. With blink off there, a blink the program asks for itself (DECSCUSR 1) is stopped too.
            // The page counts as focused only once WebView2 has handed it the keyboard, which can take a few seconds after the
            // window is activated; a focused reading asks for the focus again every half second until it arrives.
            async Task<(bool Focused, string Classes, string Animation, bool ReducedMotion)> CursorWhen(Func<(bool Focused, string Classes, string Animation, bool ReducedMotion), bool> drawn, bool focus = false)
            {
                var cursor = await terminal.ReadCursorAsync();
                for (var waited = 0; waited < (focus ? 8000 : 3000) && !drawn(cursor); waited += 75)
                {
                    if (focus && !cursor.Focused && waited % 525 == 0) { Activate(); await terminal.FocusInputAsync(); }
                    await Task.Delay(75);
                    cursor = await terminal.ReadCursorAsync();
                }
                return cursor;
            }
            static bool Has(string classes, string name) => classes.Split(' ').Contains(name);
            await ApplyTerminalOptionsAsync(preferences => { preferences.CursorStyle = "underline"; preferences.CursorBlink = true; });
            _search.Focus(FocusState.Programmatic);
            var idleUnderline = await CursorWhen(cursor => !cursor.Focused && Has(cursor.Classes, "xterm-cursor-underline"));
            await ApplyTerminalOptionsAsync(preferences => preferences.CursorStyle = "block");
            var idleBlock = await CursorWhen(cursor => !cursor.Focused && Has(cursor.Classes, "xterm-cursor-outline"));
            (bool Focused, string Classes, string Animation, bool ReducedMotion) blinking, steady, programBlink;
            bool emulationTakes;
            try
            {
                // Both answers reach the page, so the one that matters below is the page's, not this machine's.
                await terminal.EmulateReducedMotionAsync(false);
                var noPreference = !(await terminal.ReadCursorAsync()).ReducedMotion;
                await terminal.EmulateReducedMotionAsync(true);
                emulationTakes = noPreference && (await terminal.ReadCursorAsync()).ReducedMotion;
                blinking =await CursorWhen(cursor => cursor.Focused && cursor.ReducedMotion && Has(cursor.Classes, "xterm-cursor-blink") && cursor.Animation is not ("none" or ""), focus: true);
                await ApplyTerminalOptionsAsync(preferences => preferences.CursorBlink = false);
                steady = await CursorWhen(cursor => cursor.Focused && Has(cursor.Classes, "xterm-cursor-block") && !Has(cursor.Classes, "xterm-cursor-blink"), focus: true);
                await terminal.RenderAsync("\x1b[1 q");
                programBlink = await CursorWhen(cursor => cursor.Focused && Has(cursor.Classes, "xterm-cursor-blink"), focus: true);
            }
            finally
            {
                await terminal.RenderAsync("\x1b[0 q");
                await terminal.EmulateReducedMotionAsync(null);
            }
            var cursorDrawn = emulationTakes && !idleUnderline.Focused && Has(idleUnderline.Classes, "xterm-cursor-underline") && !Has(idleUnderline.Classes, "xterm-cursor-blink") && idleUnderline.Animation == "none"
                && !idleBlock.Focused && Has(idleBlock.Classes, "xterm-cursor-outline")
                && blinking.Focused && blinking.ReducedMotion && Has(blinking.Classes, "xterm-cursor-blink") && blinking.Animation.StartsWith("blink_block_", StringComparison.Ordinal)
                && steady.Focused && steady.ReducedMotion && !Has(steady.Classes, "xterm-cursor-blink") && steady.Animation == "none"
                && programBlink.Focused && programBlink.ReducedMotion && Has(programBlink.Classes, "xterm-cursor-blink") && programBlink.Animation == "none";
            if (!cursorDrawn) throw new InvalidOperationException($"The cursor was not drawn as chosen: emulationTakes={emulationTakes} unfocused underline={idleUnderline} unfocused block={idleBlock} focused blinking={blinking} focused steady={steady} program blink with Settings blink off={programBlink}.");

            // A dropped path goes in quoted and is never entered. Program Files exists everywhere, needs quoting, and fits on one
            // line at any column count. The PING typed after it lands on the same line, so the fixture must not answer it; the
            // next PING is answered, and by then the first answer would have been printed if the drop had pressed Enter.
            var dropFolder = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var dropQuoted = PathPaste.Quote(dropFolder);
            var drop = await terminal.DropTextAsync(dropFolder);
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains(dropQuoted));
            var dropUnsent = Guid.NewGuid().ToString("N");
            await terminal.SendFromRendererAsync("PING:" + dropUnsent + "\r");
            var dropAnswered = Guid.NewGuid().ToString("N");
            await terminal.SendFromRendererAsync("PING:" + dropAnswered + "\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:" + dropAnswered));
            var accent = Theme.Parse(ThemeCatalog.TerminalAccent);
            var dropPastesQuotedPath = drop.Accepted && drop.Outlined && drop.Cleared && drop.Outline == $"rgb({accent.R}, {accent.G}, {accent.B})"
                && dropQuoted != dropFolder && !(await terminal.ReadScreenAsync()).Contains("PONG:" + dropUnsent);
            if (!dropPastesQuotedPath) throw new InvalidOperationException($"A dropped path was not pasted quoted and unsent, or the outline did not show in the accent and clear: {drop}.");

            // Pastes into PowerShell (docs/decisions/clipboard.md). Control characters go, so ESC and BEL leave the line and the
            // fixture answers with the rest; one line loses the line break it was copied with and waits, so the Z typed after it
            // joins it. Several lines are shown first: Cancel sends nothing, and Paste with Don't ask again sends them and turns the
            // question off, the Settings switch with it; off, several lines go straight in. Shift+Insert and a paste the browser
            // makes itself go through the host, which pastes the clipboard, never the browser's copy of it.
            async Task<bool> ShowsAsync(string text)
            {
                for (var waited = System.Diagnostics.Stopwatch.StartNew(); waited.Elapsed < TimeSpan.FromSeconds(5); await Task.Delay(75))
                    if ((await terminal.ReadScreenAsync()).Contains(text, StringComparison.Ordinal)) return true;
                return false;
            }
            async Task<string> ScreenEndAsync() { var screen = (await terminal.ReadScreenAsync()).TrimEnd(); return screen.Length > 400 ? screen[^400..] : screen; }
            var filteredLine = Guid.NewGuid().ToString("N");
            await pane.PasteTextAsync("PING:" + filteredLine + "\x1b[31mX\x07Y \r\n");
            var filteredWaits = await ShowsAsync("PING:" + filteredLine + "[31mXY");
            await terminal.SendFromRendererAsync("Z\r");
            var pasteFilteredAndTrimmed = filteredWaits && await ShowsAsync("PONG:" + filteredLine + "[31mXYZ");
            if (!pasteFilteredAndTrimmed) throw new InvalidOperationException($"A paste into PowerShell kept a control character, or ran its one line: waited={filteredWaits} screen='{await ScreenEndAsync()}'.");

            string first = Guid.NewGuid().ToString("N"), second = Guid.NewGuid().ToString("N");
            var twoLines = "PING:" + first + "\r\nPING:" + second + "\r\n";
            var cancelled = pane.PasteTextAsync(twoLines);
            var askedFirst = await EventuallyAsync(() => _pasteQuestion is not null && _dialogsOpen == 1);
            var shownAsSent = _pasteQuestion is { Dialog.Title: "Paste 2 lines into PowerShell?", DontAsk.IsChecked: false } shownQuestion
                && shownQuestion.Text.Text == "PING:" + first + "\nPING:" + second + "\n";
            _pasteQuestion?.Dialog.Hide();
            await cancelled;
            var afterCancel = Guid.NewGuid().ToString("N");
            await terminal.SendFromRendererAsync("PING:" + afterCancel + "\r");
            var cancelSentNothing = await ShowsAsync("PONG:" + afterCancel) && !(await terminal.ReadScreenAsync()).Contains("PONG:" + first, StringComparison.Ordinal);
            var confirmed = pane.PasteTextAsync(twoLines);
            var askedAgain = await EventuallyAsync(() => _pasteQuestion is not null && _dialogsOpen == 1);
            var pasteButton = _pasteQuestion is { } open ? Within(open.Dialog).OfType<Button>().FirstOrDefault(button => button.Name == "PrimaryButton") : null;
            if (_pasteQuestion is { } answering)
            {
                answering.DontAsk.IsChecked = true;
                if (pasteButton is not null) new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(pasteButton).Invoke();
                else answering.Dialog.Hide();
            }
            await confirmed;
            var confirmedSent = await ShowsAsync("PONG:" + second) && (await terminal.ReadScreenAsync()).Contains("PONG:" + first, StringComparison.Ordinal);
            var questionTurnedOff = !_preferences.ConfirmMultiLinePaste && _confirmPasteSwitch is { IsOn: false } && !SavedPreferences(() => new AppPreferences()).ConfirmMultiLinePaste;
            string third = Guid.NewGuid().ToString("N"), fourth = Guid.NewGuid().ToString("N");
            await pane.PasteTextAsync("PING:" + third + "\nPING:" + fourth + "\n");
            var straightIn = _pasteQuestion is null && await ShowsAsync("PONG:" + fourth) && (await terminal.ReadScreenAsync()).Contains("PONG:" + third, StringComparison.Ordinal);
            SetConfirmMultiLinePaste(true);
            var multiLinePasteAsked = askedFirst && shownAsSent && cancelSentNothing && askedAgain && pasteButton is not null && confirmedSent && questionTurnedOff && straightIn
                && _confirmPasteSwitch is { IsOn: true };
            if (!multiLinePasteAsked) throw new InvalidOperationException($"Several lines pasted into PowerShell were not shown first, held on Cancel, sent on Paste, or sent straight in with the question off: asked={askedFirst} shown={shownAsSent} cancelled={cancelSentNothing} askedAgain={askedAgain} button={pasteButton is not null} sent={confirmedSent} off={questionTurnedOff} straight={straightIn} screen='{await ScreenEndAsync()}'.");

            static void ToClipboard(string text)
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
            var viaKey = Guid.NewGuid().ToString("N");
            ToClipboard("PING:" + viaKey);
            var insertTaken = await terminal.PressKeyAsync("Insert", shift: true);
            var insertPasted = await ShowsAsync("PING:" + viaKey);
            await terminal.SendFromRendererAsync("\r");
            insertPasted = insertPasted && await ShowsAsync("PONG:" + viaKey);
            string viaEvent = Guid.NewGuid().ToString("N"), browserCopy = Guid.NewGuid().ToString("N");
            ToClipboard("PING:" + viaEvent);
            var eventStopped = await terminal.PasteEventAsync("PING:" + browserCopy);
            var eventPasted = await ShowsAsync("PING:" + viaEvent);
            await terminal.SendFromRendererAsync("\r");
            eventPasted = eventPasted && await ShowsAsync("PONG:" + viaEvent) && !(await terminal.ReadScreenAsync()).Contains(browserCopy, StringComparison.Ordinal);
            var pasteThroughHost = insertTaken && insertPasted && eventStopped && eventPasted;
            if (!pasteThroughHost) throw new InvalidOperationException($"Shift+Insert or a paste the browser made did not go through SIC's paste: insertTaken={insertTaken} insertPasted={insertPasted} eventStopped={eventStopped} eventPasted={eventPasted} screen='{await ScreenEndAsync()}'.");

            var single = await terminal.GetDimensionsAsync();
            // A split tree. Right of the live pane, down of the new one, down of the first: the 2×2 of the old grid. Then right of
            // the bottom-left pane, which halves it; a second split of that half is refused, since two 160 px panes and a divider
            // do not fit in its width; and down of the bottom-right pane. Six panes, each running a fixture shell, tiled as the
            // tree says after every split. The first pane loses width to the second split and height to the third.
            async Task<SessionPane> SplitStartedAsync(SessionPane source, PaneSplit direction, int number)
            {
                var added = AddPane(shellHost, new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · split check " + number, data, ""), source, direction);
                await added.StartAsync(CreateShellRequest(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", SmokeScript], data), SmokeStart);
                await WaitForAsync(async () => (await added.Terminal!.ReadScreenAsync()).Contains("READY"));
                return added;
            }
            // The splits below check that a split halves one pane and moves no other, so the tab's Keep panes even is turned off
            // first; the choice itself is checked once the tab is back to one pane.
            SetKeepEven(shellHost, false);
            var splitTiled = true;
            var paneB = await SplitStartedAsync(pane, PaneSplit.Columns, 2); splitTiled &= PanesTiled(shellHost);
            var paneC = await SplitStartedAsync(paneB, PaneSplit.Rows, 3); splitTiled &= PanesTiled(shellHost);
            var paneD = await SplitStartedAsync(pane, PaneSplit.Rows, 4); splitTiled &= PanesTiled(shellHost);
            var paneE = await SplitStartedAsync(paneD, PaneSplit.Columns, 5); splitTiled &= PanesTiled(shellHost);
            _notice.IsOpen = false; LastNotice = null;
            var eTooNarrow = !paneE.FitsSplit(PaneSplit.Columns) && paneE.FitsSplit(PaneSplit.Rows) && paneE.CanSplit;
            await SplitAsync(shellHost, paneE, PaneSplit.Columns);
            var splitRefused = eTooNarrow && shellHost.Panes.Count == 5 && _notice.IsOpen && _notice.Title == "This pane is too narrow to split" && LastNotice is { Brief: true }
                && (_notice.Message ?? "").Contains("Two side-by-side panes need ", StringComparison.Ordinal);
            _notice.IsOpen = false;
            var paneF = await SplitStartedAsync(paneC, PaneSplit.Rows, 6); splitTiled &= PanesTiled(shellHost);
            await WaitForAsync(async () => await terminal.GetDimensionsAsync() is var quarter && quarter.Columns < single.Columns && quarter.Rows < single.Rows);
            var splitPathsFit = shellHost.Panes.All(item => item.PathFitsBest());
            // In tree order: each new pane follows the one it split.
            var splitOrder = shellHost.Panes.SequenceEqual([pane, paneD, paneE, paneB, paneC, paneF]);
            var splitCreated = shellHost.Panes.Count == 6 && shellHost.Content.Dividers.Count == 5 && splitOrder
                && shellHost.Panes.All(item => item.IsRunning && item.CanSplit) && TabColorFollowsSelection(shellHost);
            SaveLayout();
            var splitSaved = SavedLayout(() => new WorkspaceLayout()).Tabs.Any(tab => tab.Id == shellHost.Saved.Id && tab.Panes.Count == 6 && tab.SplitRatio is null && tab.RowRatio is null
                && tab.Layout is { } savedTree && PaneLayout.Splits(savedTree).Count == 5 && PaneLayout.Leaves(savedTree).SequenceEqual(tab.Panes.Select(item => item.Id)));
            var splitShellImage = await CaptureShellAsync(Path.Combine(data, "split-native.png"), terminal);

            // Alt+Arrow from inside the live terminals, through the page's chord table, around the six panes: the first pane over
            // the two halves on the left, the second over the third over the sixth on the right. Each press lands on the pane across
            // that divider and gives it the keyboard; toward the tab's edge the page leaves the key to the terminal. The right-hand
            // half has the third and sixth panes across its right edge: the one focused last is chosen, so a round trip comes back
            // (the tree's order alone would pick the third both times).
            var focusMisses = new List<string>();
            async Task PressAltArrowAsync(SessionPane from, string key, SessionPane? to)
            {
                var step = $"{from.NameText} {key} → {to?.NameText ?? "the terminal"}";
                try
                {
                    if (_activePane != from) { focusMisses.Add($"{step} (the focus was on {_activePane?.NameText})"); return; }
                    var sides = AltArrowSides(shellHost.Content.Tree, from.Descriptor.Id);
                    await WaitForAsync(async () => await from.Terminal!.ReadNeighborsAsync() == sides);
                    var taken = await from.Terminal!.PressKeyAsync(key, alt: true);
                    if (to is null) { if (taken || _activePane != from) focusMisses.Add(step); return; }
                    await WaitForAsync(() => Task.FromResult(_activePane == to && to.Terminal!.HasFocus));
                    if (!taken) focusMisses.Add(step + " (the page did not take the key)");
                }
                catch (OperationCanceledException) { focusMisses.Add($"{step} (timed out on {_activePane?.NameText})"); }
            }
            SetActivePane(pane);
            foreach (var (from, key, to) in new (SessionPane, string, SessionPane?)[]
            {
                (pane, "ArrowRight", paneB), (paneB, "ArrowDown", paneC), (paneC, "ArrowLeft", paneE), (paneE, "ArrowLeft", paneD), (paneD, "ArrowUp", pane),
                (pane, "ArrowLeft", null),
                (pane, "ArrowDown", paneD), (paneD, "ArrowRight", paneE), (paneE, "ArrowRight", paneC), (paneC, "ArrowDown", paneF), (paneF, "ArrowLeft", paneE), (paneE, "ArrowRight", paneF),
                (paneF, "ArrowRight", null), (paneF, "ArrowDown", null)
            }) await PressAltArrowAsync(from, key, to);
            // A tab shown again gives the focus back to the pane focused last in it, the sixth, not to its first, and its terminal
            // takes the keyboard once the tab's panes have rejoined the window (focus given at the switch itself was lost).
            var focusCheckHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "focus check", [new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · focus check", data, "")]));
            _tabs.SelectedItem = shellHost.Item;
            var tabRemembersPane = true;
            try { await WaitForAsync(() => Task.FromResult(SelectedHost == shellHost && _activePane == paneF && paneF.Terminal!.HasFocus)); }
            catch (OperationCanceledException) { tabRemembersPane = false; }
            // Alt+Up from the top row goes up to the tab strip: from the sixth pane up to the third, the second, then the tab. On
            // the strip the window's Alt+Arrow keys (driven through what they call) show the tab beside with the keyboard kept on
            // the strip, stop at its end, go into a tab (the check tab's placeholder gives its Start terminal button the keyboard),
            // come back up from a placeholder, and go back into the six panes at the pane last focused there. Then Alt and the check
            // tab's number from a terminal shows it, and Alt with the keypad's 1 is left to the terminal, for Windows' Alt codes.
            await PressAltArrowAsync(paneF, "ArrowUp", paneC);
            await PressAltArrowAsync(paneC, "ArrowUp", paneB);
            var stripMisses = new List<string>();
            TabViewItem? StripFocus() => FocusManager.GetFocusedElement(_root.XamlRoot) is TabViewItem { FocusState: FocusState.Keyboard } item ? item : null;
            async Task ExpectAsync(string step, Func<bool> condition)
            {
                try { await WaitForAsync(() => Task.FromResult(condition())); }
                catch (OperationCanceledException) { stripMisses.Add(step); }
            }
            var upTaken = await paneB.Terminal!.PressKeyAsync("ArrowUp", alt: true);
            await ExpectAsync("Alt+Up from the top row to the tab strip", () => upTaken && StripFocus() == shellHost.Item);
            var stripFocusImage = await CaptureShellAsync(Path.Combine(data, "strip-focus-native.png"), paneB.Terminal!);
            var movedRight = MoveFocus(PaneDirection.Right);
            await ExpectAsync("Alt+Right on the strip to the next tab", () => movedRight && SelectedHost == focusCheckHost && StripFocus() == focusCheckHost.Item);
            if (MoveFocus(PaneDirection.Right) || SelectedHost != focusCheckHost || StripFocus() != focusCheckHost.Item) stripMisses.Add("Alt+Right at the end of the strip");
            var intoPlaceholder = MoveFocus(PaneDirection.Down);
            await ExpectAsync("Alt+Down into the check tab's placeholder", () => intoPlaceholder && focusCheckHost.Panes[0].HoldsFocus() && FocusManager.GetFocusedElement(_root.XamlRoot) is Button);
            var upFromPlaceholder = MoveFocus(PaneDirection.Up);
            await ExpectAsync("Alt+Up from a placeholder to the strip", () => upFromPlaceholder && StripFocus() == focusCheckHost.Item);
            var movedLeft = MoveFocus(PaneDirection.Left);
            await ExpectAsync("Alt+Left on the strip back to the six panes, the keyboard kept on the strip", () => movedLeft && SelectedHost == shellHost && StripFocus() == shellHost.Item);
            // F2 there (what its accelerator calls) renames the tab with the focus, though the tab is split.
            RenameFocused();
            await ExpectAsync("F2 on the strip renaming the focused tab", () => shellHost.Editor is not null && shellHost.Panes.All(item => item.Editor is null));
            await EndRenameAsync(shellHost, commit: false, refocus: false);
            shellHost.Item.Focus(FocusState.Keyboard);
            var backIn = MoveFocus(PaneDirection.Down);
            await ExpectAsync("Alt+Down back into the pane last focused, the second", () => backIn && _activePane == paneB && paneB.Terminal!.HasFocus);
            // The check tab is the last, and earlier checks may have left tabs before the six-pane one.
            var checkNumber = Math.Min(9, _tabs.TabItems.IndexOf(focusCheckHost.Item) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var altDigitTaken = await paneB.Terminal!.PressKeyAsync(checkNumber, alt: true, code: "Digit" + checkNumber);
            await ExpectAsync($"Alt+{checkNumber} from a terminal to the check tab", () => altDigitTaken && SelectedHost == focusCheckHost);
            if (await paneB.Terminal!.PressKeyAsync("1", alt: true, code: "Numpad1")) stripMisses.Add("Alt with the keypad's 1 was taken");
            _tabs.SelectedItem = shellHost.Item;
            await CloseTabAsync(focusCheckHost.Item);
            var paneFocusMoved = focusMisses.Count == 0 && tabRemembersPane && stripMisses.Count == 0;
            if (!paneFocusMoved)
                throw new InvalidOperationException($"Alt+Arrow did not move the focus between the six panes and the tab strip as laid out, or a tab shown again did not focus its last pane: {string.Join("; ", focusMisses.Concat(stripMisses))} tabRemembersPane={tabRemembersPane} (on {_activePane?.NameText}).");

            // Pane names. The bottom-left half is renamed through its in-place editor (opened on its current name, a trimmed name
            // saved on the pane itself, since a shell has no conversation to keep it against) and the tab, which has no name of its
            // own, still goes by its first pane. Then F2 from inside the first pane's terminal renames that pane, the tab being split,
            // and the tab follows it; Ctrl+F2 from the same terminal opens the tab's own rename instead. Both names are in the saved layout.
            paneE.BeginRename();
            var paneEditor = paneE.Editor ?? throw new InvalidOperationException("Renaming a pane did not open its in-place editor.");
            var paneEditorShown = paneEditor.Text == "split check 5" && paneE.NameText == "split check 5";
            paneEditor.Text = "  Build watch  ";
            await paneE.EndRenameAsync(commit: true, refocus: false);
            var paneNamed = paneEditorShown && paneE.Editor is null && paneE.Descriptor.CustomTitle == "Build watch" && paneE.NameText == "Build watch"
                && paneE.DisplayName == "Build watch" && shellHost.Title.Text == "integration check" && shellHost.Saved.CustomTitle is null;
            SetActivePane(pane);
            var f2PaneTaken = await terminal.PressKeyAsync("F2");
            await WaitForAsync(() => Task.FromResult(pane.Editor is not null));
            var f2RenamedPane = shellHost.Editor is null;
            pane.Editor!.Text = "First pane";
            await pane.EndRenameAsync(commit: true, refocus: true);
            var firstNamed = f2PaneTaken && f2RenamedPane && pane.Editor is null && pane.Descriptor.CustomTitle == "First pane" && pane.NameText == "First pane" && shellHost.Title.Text == "First pane"
                && shellHost.Saved.CustomTitle is null && ToolTipService.GetToolTip(shellHost.Item) is string namedTip && namedTip.StartsWith("First pane\n", StringComparison.Ordinal);
            var ctrlF2TabTaken = await terminal.PressKeyAsync("F2", ctrl: true);
            await WaitForAsync(() => Task.FromResult(shellHost.Editor is not null));
            var ctrlF2RenamedTab = ctrlF2TabTaken && pane.Editor is null;
            await EndRenameAsync(shellHost, commit: false, refocus: false);
            var namesSaved = SavedLayout(() => new WorkspaceLayout()).Tabs.Any(tab => tab.Id == shellHost.Saved.Id && tab.CustomTitle is null
                && tab.Panes.Count(item => item.CustomTitle == "Build watch") == 1 && tab.Panes[0].CustomTitle == "First pane");
            if (!paneNamed || !firstNamed || !ctrlF2RenamedTab || !namesSaved)
                throw new InvalidOperationException($"Renaming panes in place did not name the pane, leave the tab to its first pane, or save, or Ctrl+F2 did not open the tab's rename: editor={paneEditorShown} named={paneNamed} f2={f2PaneTaken} first={firstNamed} ctrlF2Tab={ctrlF2RenamedTab} saved={namesSaved} tab='{shellHost.Title.Text}'.");

            // Settings → Session Indicator Width (docs/decisions/pane-border.md), each width from 1 to 8 px set by moving its slider,
            // shown beside it and saved. In the six live panes the one with the keyboard wears the accent border at that width and
            // the others wear none, and no terminal is resized, since the border lies over the page's margin. The window as Windows
            // composes it, the live terminals included (PrintWindow; RenderTargetBitmap leaves a WebView2 out), shows the accent at
            // the first pane's left edge, the terminal's own background just inside the border, and none at the second pane's edge.
            // Each width is captured; the default is put back.
            var borderMisses = new List<string>();
            // The tab rename just closed can leave the panes a layout pass from rejoining the window, the terminal 0 wide until then.
            await WaitForAsync(() => Task.FromResult(terminal.ActualWidth > 0));
            var sizeBeforeBorders = await terminal.GetDimensionsAsync();
            var terminalWidthBeforeBorders = terminal.ActualWidth;
            var paneBorderImages = new List<string>();
            string Hex(Windows.UI.Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";
            for (var width = PaneBorder.Min; width <= PaneBorder.Max; width++)
            {
                var choice = width + " px";
                _paneBorderSlider!.Value = width;
                if (!shellHost.Panes.All(item => item.RingWidth == width) || SavedPreferences(() => new AppPreferences()).PaneBorderWidth != width || _paneBorderValue?.Text != choice)
                    borderMisses.Add(choice + ": not applied, not shown beside the slider or not saved");
                if (_activePane != pane || pane.RingLine.BorderBrush is not Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var ring } || ring != _theme.Accent.Color
                    || shellHost.Panes.Where(item => item != pane).Any(item => item.RingLine.BorderBrush is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: > 0 }))
                    borderMisses.Add(choice + ": the accent was not on the first pane alone");
                await Task.Delay(250);
                var imagePath = Path.Combine(data, $"pane-border-{width}px.png");
                if (await CaptureWindowAsync(imagePath) is not { } window) { borderMisses.Add(choice + ": the window could not be captured"); continue; }
                paneBorderImages.Add(imagePath);
                var scale = _root.XamlRoot!.RasterizationScale;
                string EdgeColor(SessionPane item, int inward)
                {
                    var edge = _root.XamlRoot!.CoordinateConverter.ConvertLocalToScreen(item.RingLine.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, item.RingLine.ActualHeight / 2)));
                    return ColorAt((window.Width, window.Height, window.Bgra), 1, edge.X - window.Left + inward, edge.Y - window.Top);
                }
                var onBorder = EdgeColor(pane, (int)Math.Floor(width * scale / 2));
                var insideBorder = EdgeColor(pane, (int)Math.Ceiling(width * scale) + (int)Math.Ceiling(2 * scale));
                var otherEdge = EdgeColor(paneB, 0);
                if (!ColorsNear(onBorder, Hex(_theme.Accent.Color), 12) || !ColorsNear(insideBorder, FixedTerminalBackground, 12) || !ColorsNear(otherEdge, FixedTerminalBackground, 12))
                    borderMisses.Add($"{choice}: on the border {onBorder} (accent {Hex(_theme.Accent.Color)}), inside it {insideBorder} and at the second pane's edge {otherEdge} (terminal {FixedTerminalBackground})");
            }
            // No reflow: the same columns and rows, and the same width to within layout rounding.
            var sizeAfterBorders = await terminal.GetDimensionsAsync();
            if (sizeAfterBorders != sizeBeforeBorders || Math.Abs(terminal.ActualWidth - terminalWidthBeforeBorders) > 0.5)
                borderMisses.Add($"the terminal was resized: {sizeBeforeBorders} to {sizeAfterBorders}, {terminalWidthBeforeBorders:0.###} to {terminal.ActualWidth:0.###} DIPs wide");
            // The border's color (October 18, 2026): a preset paints its dark shade on the active pane and is saved; a custom color
            // joins the recent swatches; cleared, the theme's accent is back.
            SetPaneBorderColor("pink");
            if (pane.RingLine.BorderBrush is not Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var pink } || pink != Theme.Parse("F472B6")
                || SavedPreferences(() => new AppPreferences()).PaneBorderColor != "pink" || pane.RingColor != Theme.Parse("F472B6"))
                borderMisses.Add("the Pink preset was not painted on the active pane or not saved");
            var recentBeforeBorderColor = _preferences.RecentTabColors.ToList();
            SetPaneBorderColor("123456");
            if (pane.RingLine.BorderBrush is not Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var custom } || custom != Theme.Parse("123456") || !_preferences.RecentTabColors.Contains("123456"))
                borderMisses.Add("a custom color was not painted or did not join the recent swatches");
            // The recent swatches as they were, which the restore stage expects.
            _preferences.RecentTabColors = recentBeforeBorderColor;
            // One of the theme's own colors (November 12, 2026) paints the theme's shade and stays out of the recent swatches.
            SetPaneBorderColor("theme-blue");
            if (pane.RingColor != Theme.Parse(TabColors.ThemeShade("theme-blue", _theme.Definition)!) || _preferences.PaneBorderColor != "theme-blue" || !_preferences.RecentTabColors.SequenceEqual(recentBeforeBorderColor))
                borderMisses.Add("a theme color was not painted in the theme's shade, not kept, or joined the recent swatches");
            SetPaneBorderColor(null);
            if (pane.RingLine.BorderBrush is not Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var accentBack } || accentBack != _theme.Accent.Color || _preferences.PaneBorderColor is not null
                || _paneBorderColorButton?.Content is not Microsoft.UI.Xaml.Shapes.Ellipse { Fill: Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var dot } } || dot != _theme.Accent.Color)
                borderMisses.Add("clearing the color did not bring the accent back on the pane and the button");
            SetPaneBorder(PaneBorder.Default);
            var paneBorder = borderMisses.Count == 0 && pane.RingWidth == PaneBorder.Default && _paneBorderSlider?.Value == PaneBorder.Default;
            if (!paneBorder) throw new InvalidOperationException("Settings → Session Indicator Width was wrong: " + string.Join("; ", borderMisses) + ".");

            // Closing panes hands each one's room to the other side of its split and re-tiles the rest; a live terminal that moved
            // must keep working. Top-right first: the pane below it takes the right column with the one below that.
            foreach (var closing in new[] { paneB, paneC, paneF, paneE, paneD })
            {
                await closing.Terminal!.SendFromRendererAsync("EXIT\r");
                await closing.Terminal.StopAsync();
                await ClosePaneAsync(shellHost, closing);
                splitTiled &= PanesTiled(shellHost) && shellHost.Panes.All(item => item.CanSplit) && !shellHost.Panes.Contains(closing);
                if (shellHost.Panes.Count != 5) continue;
                var moved = Guid.NewGuid().ToString("N");
                await paneF.Terminal!.SendFromRendererAsync("PING:" + moved + "\r");
                await WaitForAsync(async () => (await paneF.Terminal!.ReadScreenAsync()).Contains("PONG:" + moved));
            }
            var whole = await WaitForSizeAsync(terminal, size => size == single);
            if (whole != single)
                throw new InvalidOperationException($"Closed back to one pane, the first terminal was {whole.Columns}×{whole.Rows}, not the {single.Columns}×{single.Rows} it had before the splits (scaling {_root.XamlRoot?.RasterizationScale}).");
            if (!splitCreated || !splitSaved || !splitTiled || shellHost.Panes.Count != 1 || !pane.IsRunning || shellHost.Content.Dividers.Count != 0)
                throw new InvalidOperationException($"Splitting the tab to six panes, tiling them, saving the tree, and closing back to one did not preserve the first terminal: created={splitCreated} saved={splitSaved} tiled={splitTiled} panes={shellHost.Panes.Count}.");
            // Keep panes even (docs/decisions/keep-panes-even.md): on, a third pane makes thirds where a split alone leaves a half and
            // two quarters; a divider drag turns it off and the dragged ratio outlives the next split; ticked again it evens the tab,
            // and the choice is saved with the tab. The panes are placeholders: the tiling is the tree's, started or not.
            SetKeepEven(shellHost, true);
            SavedPane EvenPane(int number) => new(Guid.NewGuid().ToString("N"), "shell", "PowerShell · even " + number, data, "");
            var evenB = AddPane(shellHost, EvenPane(2), pane, PaneSplit.Columns);
            var evenC = AddPane(shellHost, EvenPane(3), evenB, PaneSplit.Columns);
            var evenThirds = shellHost.Saved.KeepEven == true && shellHost.Panes.All(item => item.KeepEven) && PanesTiled(shellHost)
                && shellHost.Saved.Layout is { Ratio: { } thirdShare, Second.Ratio: 0.5 } && Math.Abs(thirdShare - 1.0 / 3) < 1e-9;
            shellHost.Content.DragTo("", 0.6);
            var evenDragged = shellHost.Saved.KeepEven == false && !shellHost.Panes.Any(item => item.KeepEven) && shellHost.Saved.Layout?.Ratio == 0.6;
            var evenD = AddPane(shellHost, EvenPane(4), evenC, PaneSplit.Rows);
            var evenKept = shellHost.Saved.Layout is { Ratio: 0.6, Second: { Ratio: 0.5, Second.Ratio: 0.5 } } && PanesTiled(shellHost);
            SetKeepEven(shellHost, true);
            SaveLayout();
            var evenAgain = shellHost.Saved.Layout is { Ratio: { } quarter, Second: { Ratio: { } inner, Second.Ratio: 0.5 } } && Math.Abs(quarter - 0.25) < 1e-9 && Math.Abs(inner - 1.0 / 3) < 1e-9
                && PanesTiled(shellHost) && shellHost.Panes.All(item => item.KeepEven)
                && SavedLayout(() => new WorkspaceLayout()).Tabs.Any(tab => tab.Id == shellHost.Saved.Id && tab.KeepEven == true && tab.Layout?.Ratio == quarter);
            foreach (var extra in new[] { evenB, evenC, evenD }) await ClosePaneAsync(shellHost, extra);
            var panesEven = evenThirds && evenDragged && evenKept && evenAgain && shellHost.Panes.Count == 1 && pane.IsRunning && shellHost.Content.Dividers.Count == 0;
            if (!panesEven)
                throw new InvalidOperationException($"Keep panes even did not make thirds, stop at a divider drag, keep the dragged ratio through a split, even the tab again when ticked, and save the choice: thirds={evenThirds} dragged={evenDragged} kept={evenKept} again={evenAgain} panes={shellHost.Panes.Count}.");
            if (!splitRefused) throw new InvalidOperationException($"A split of a pane too narrow for it was not refused with a brief notice: narrow={eTooNarrow} panes={shellHost.Panes.Count} notice='{LastNotice?.Title}'.");
            // Back to one pane, every side but the top is the tab's edge: Alt+Right and Alt+Down reach the terminal, and Alt+Up still
            // goes up to the tab strip, from which Down comes back in.
            try
            {
                await WaitForAsync(async () => await terminal.ReadNeighborsAsync() == AltArrowSides(shellHost.Content.Tree, pane.Descriptor.Id));
                paneFocusMoved &= !await terminal.PressKeyAsync("ArrowRight", alt: true) && !await terminal.PressKeyAsync("ArrowDown", alt: true) && await terminal.PressKeyAsync("ArrowUp", alt: true);
                await WaitForAsync(() => Task.FromResult(ReferenceEquals(FocusManager.GetFocusedElement(_root.XamlRoot), shellHost.Item)));
                paneFocusMoved &= EnterTab(shellHost.Item);
                await WaitForAsync(() => Task.FromResult(terminal.HasFocus));
            }
            catch (OperationCanceledException) { paneFocusMoved = false; }
            if (!paneFocusMoved) throw new InvalidOperationException($"Back to one pane, Alt+Right or Alt+Down was taken instead of reaching the terminal, or Alt+Up did not reach the tab strip and come back: the page held {await terminal.ReadNeighborsAsync()}.");

            // A saved conversation opened in a split of the current tab goes beside the focused pane (the wide first pane splits
            // into columns) as a placeholder; renamed, the name is kept as the conversation's local name, which Sessions lists it
            // under, and not on the pane. Closed again, the tab is back to one pane.
            var olderConversation = _catalog.Get("claude:" + SmokeOlderSessionId) ?? throw new InvalidOperationException("The older fixture is not in the catalog.");
            SetActivePane(pane);
            var linkedPane = await OpenConversationAsync(olderConversation, split: true) ?? throw new InvalidOperationException("The older fixture did not open in a split.");
            var linkedPlaced = shellHost.Panes.Count == 2 && shellHost.Panes[1] == linkedPane && linkedPane.Terminal is null && PanesTiled(shellHost)
                && shellHost.Content.Arrangement?.Panes[1].Rect.X > 0 && shellHost.Content.Arrangement?.Panes[1].Rect.Y == 0;
            await RenamePaneAsync(linkedPane, " Older notes ");
            await RefreshHistoryAsync();
            _search.Text = "older notes"; await SearchAsync();
            var linkedNamed = _preferences.ConversationNames.GetValueOrDefault("claude:" + SmokeOlderSessionId) == "Older notes" && linkedPane.Descriptor.CustomTitle is null
                && linkedPane.NameText == "Older notes" && shellHost.Title.Text == "First pane"
                && _history.Items.Count == 1 && RowConversation(_history, 0).SessionId == SmokeOlderSessionId && RowConversation(_history, 0).Title == "Older notes";
            _search.Text = ""; await SearchAsync();
            await ClosePaneAsync(shellHost, linkedPane);
            var paneRenamed = paneNamed && firstNamed && namesSaved && linkedPlaced && linkedNamed && shellHost.Panes.Count == 1 && pane.IsRunning;
            if (!paneRenamed) throw new InvalidOperationException($"A conversation opened in a split was not placed beside the focused pane, or renaming it did not name the conversation: placed={linkedPlaced} named={linkedNamed} panes={shellHost.Panes.Count}.");

            // Ctrl+Shift+D from inside the live terminal, through the page's chord table: a new tab running a new session of the
            // same agent in the same folder, with neither the name nor the color copied, and the original still answering.
            var tabsBeforeDuplicate = _open.Count;
            var duplicateOffered = shellHost.Item.ContextFlyout is MenuFlyout tabMenu && tabMenu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Duplicate tab" && item.IsEnabled);
            var duplicateTaken = await terminal.PressKeyAsync("D", ctrl: true, shift: true);
            await WaitForAsync(() => Task.FromResult(_open.Count == tabsBeforeDuplicate + 1 && SelectedHost is { } copy && copy != shellHost && copy.Panes[0].IsRunning));
            var duplicateHost = SelectedHost!;
            var duplicatePane = duplicateHost.Panes[0];
            await WaitForAsync(async () => (await duplicatePane.Terminal!.ReadScreenAsync()).Contains("READY"));
            var duplicateNonce = Guid.NewGuid().ToString("N");
            await duplicatePane.Terminal!.SendFromRendererAsync("PING:" + duplicateNonce + "\r");
            await WaitForAsync(async () => (await duplicatePane.Terminal!.ReadScreenAsync()).Contains("PONG:" + duplicateNonce));
            await terminal.SendFromRendererAsync("PING:" + duplicateNonce + "\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:" + duplicateNonce));
            var tabDuplicated = duplicateOffered && duplicateTaken && duplicateHost.Panes.Count == 1 && pane.IsRunning
                && duplicatePane.Descriptor.AgentId == pane.Descriptor.AgentId && duplicatePane.Descriptor.WorkingDirectory == pane.Descriptor.WorkingDirectory
                && duplicatePane.Descriptor.Id != pane.Descriptor.Id && duplicatePane.Descriptor.SessionId is null
                && duplicateHost.Saved.Color is null && duplicateHost.Saved.CustomTitle is null && shellHost.Saved.Color == "pink";
            await duplicatePane.Terminal.SendFromRendererAsync("EXIT\r");
            await duplicatePane.Terminal.StopAsync();
            await CloseTabAsync(duplicateHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!tabDuplicated) throw new InvalidOperationException($"Duplicating the tab did not start a new session of the same agent in the same folder without its name or color, or the original stopped answering: offered={duplicateOffered} taken={duplicateTaken}.");

            // A tab dropped into another (MainWindow.TabDrops.cs). The drop is worked out from the pointer's place over the showing
            // tab's canvas: the pane under it, the side of it nearest in shares of its size, and the half that side would take. A tab
            // is refused into itself. The mark a drag shows covers that half and names the tab, and clears. A hover on another tab's
            // header during the drag shows that tab, and a drag ending in the strip shows the dragged tab again. Then the drop is
            // made as the drag's end makes it: a tab running a fixture shell goes in below the first pane, tiled as rows, the emptied
            // tab leaves the strip, both terminals still answer (the moved one was taken from one canvas to the other), the moved
            // pane's header takes the tab's paint and the keyboard, and the layout is saved without the moved tab. Last, a tab
            // dropped left of another's first pane: that tab, called after its first pane, keeps the name as its own.
            var movingSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · moving tab", data, "");
            var movingHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), movingSaved.Title, [movingSaved]));
            var movingPane = movingHost.Panes[0];
            await movingPane.StartAsync(CreateShellRequest(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", SmokeScript], data), SmokeStart);
            await WaitForAsync(async () => (await movingPane.Terminal!.ReadScreenAsync()).Contains("READY"));
            _tabs.SelectedItem = shellHost.Item;
            var shellCanvas = shellHost.Content;
            await WaitForAsync(() => Task.FromResult(SelectedHost == shellHost && shellCanvas.ActualWidth > 0 && shellCanvas.Arrangement is { Panes.Count: 1 }));
            var canvasWidth = shellCanvas.ActualWidth; var canvasHeight = shellCanvas.ActualHeight;
            var bottomDrop = shellCanvas.DropAt(new Windows.Foundation.Point(canvasWidth / 2, canvasHeight - 10));
            var rightDrop = shellCanvas.DropAt(new Windows.Foundation.Point(canvasWidth - 10, canvasHeight / 2));
            var leftDrop = shellCanvas.DropAt(new Windows.Foundation.Point(10, canvasHeight / 2));
            var topDrop = shellCanvas.DropAt(new Windows.Foundation.Point(canvasWidth / 2, 10));
            var bottomArea = PaneLayout.DropArea(shellCanvas.Arrangement!.Panes[0].Rect, PaneDirection.Down, PaneSplitter.GrabSize);
            var dropSides = bottomDrop is { Side: PaneDirection.Down } && bottomDrop.Pane == pane.Descriptor.Id && bottomDrop.Area == bottomArea
                && rightDrop is { Side: PaneDirection.Right } && leftDrop is { Side: PaneDirection.Left } && topDrop is { Side: PaneDirection.Up };
            var dropRefusedIntoItself = DropRefusal(shellHost, shellHost, bottomDrop) == "This is the tab being moved" && DropRefusal(movingHost, shellHost, bottomDrop) is null
                && DropRefusal(movingHost, shellHost, null) == "No pane here";
            shellCanvas.ShowDrop(bottomDrop!.Area, "moving tab", allowed: true);
            shellCanvas.UpdateLayout();
            var markShown = shellCanvas.DropShown is { Allowed: true, Label: "moving tab" } shownMark && shownMark.Area == bottomDrop.Area && PanesTiled(shellHost);
            shellCanvas.HideDrop();
            var markHidden = shellCanvas.DropShown is null;
            BeginTabDragForCheck(shellHost);
            // The tab under the pointer is found by place in the strip, since a reorder's drag events come from the strip's list.
            var movingOrigin = movingHost.Item.TransformToVisual(_tabs).TransformPoint(new Windows.Foundation.Point(0, 0));
            var tabFoundByPlace = TabAt(new Windows.Foundation.Point(movingOrigin.X + movingHost.Item.ActualWidth / 2, movingOrigin.Y + movingHost.Item.ActualHeight / 2)) == movingHost.Item
                && TabAt(new Windows.Foundation.Point(-5, -5)) is null;
            // The hover, with no wait: a first reading over the moving tab's header begins it, a second completes it and shows the tab;
            // a reading over the dragged tab's own slot or the tab showing begins none.
            var movingCentre = new Windows.Foundation.Point(movingOrigin.X + movingHost.Item.ActualWidth / 2, movingOrigin.Y + movingHost.Item.ActualHeight / 2);
            var hoverWait = DragHoverToShow;
            DragHoverToShow = TimeSpan.Zero;
            HoverAt(movingCentre, "check");
            var hoverBegun = SelectedHost == shellHost;
            HoverAt(movingCentre, "check");
            DragHoverToShow = hoverWait;
            var hoverShown = tabFoundByPlace && hoverBegun && SelectedHost == movingHost;
            EndTabDragForCheck();
            var hoverReturned = SelectedHost == shellHost && _open.ContainsKey(movingHost.Item) && _open.ContainsKey(shellHost.Item);
            var tabsBeforeMove = _open.Count;
            BeginTabDragForCheck(movingHost);
            EndTabDragForCheck(shellHost, bottomDrop);
            await WaitForAsync(() => Task.FromResult(shellHost.Content.ActualHeight > 0 && movingPane.ActualHeight > 0 && movingPane.ActualHeight < canvasHeight));
            var tabMovedIn = _open.Count == tabsBeforeMove - 1 && !_open.ContainsKey(movingHost.Item) && !_tabs.TabItems.Contains(movingHost.Item) && movingHost.Panes.Count == 0
                && shellHost.Panes.SequenceEqual([pane, movingPane]) && shellHost.Saved.Layout is { } movedTree && PaneLayout.DirectionOf(movedTree) == PaneSplit.Rows
                && PaneLayout.Leaves(movedTree).SequenceEqual([pane.Descriptor.Id, movingPane.Descriptor.Id]) && shellHost.Saved.Panes.Select(item => item.Id).SequenceEqual(PaneLayout.Leaves(movedTree))
                && _activePane == movingPane && SelectedHost == shellHost && ReferenceEquals(movingPane.HeaderFill, shellHost.Paint.Selected) && PanesTiled(shellHost)
                && shellHost.Saved.CustomTitle is null && shellHost.Title.Text == "First pane" && movingPane.IsRunning && pane.IsRunning;
            var movedNonce = Guid.NewGuid().ToString("N");
            await movingPane.Terminal!.SendFromRendererAsync("PING:" + movedNonce + "\r");
            await WaitForAsync(async () => (await movingPane.Terminal!.ReadScreenAsync()).Contains("PONG:" + movedNonce));
            await terminal.SendFromRendererAsync("PING:" + movedNonce + "\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:" + movedNonce));
            var movedFocused = await EventuallyAsync(() => movingPane.Terminal!.HasFocus);
            var movePersisted = SavedLayout(() => new WorkspaceLayout()).Tabs is { } tabsAfterMove && tabsAfterMove.All(tab => tab.Id != movingHost.Saved.Id)
                && tabsAfterMove.Any(tab => tab.Id == shellHost.Saved.Id && tab.Panes.Count == 2 && tab.Panes[1].Id == movingPane.Descriptor.Id && tab.Layout is { } moveTree && PaneLayout.DirectionOf(moveTree) == PaneSplit.Rows);
            // The reverse: Move pane to new tab takes the moved pane back out into a tab of its own, right after the shell tab, with
            // its terminal running and answering, the shell tab back to one pane (its only pane may not be moved out), and both saved.
            var movedOutOffered = movingPane.CanMoveOut && pane.CanMoveOut && shellHost.Item.ContextFlyout is MenuFlyout outMenu && outMenu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Move pane to new tab");
            // A pane dragged by its header (MainWindow.PaneDrags.cs), driven through what the pointer calls. First within its own
            // tab: the moved pane, below the first, dragged to the first pane's right edge shows the mark over the right half with the
            // pane's name and lands there, the tab now columns. Dragged over itself the mark refuses without words and a release moves
            // nothing. Then up to the strip just after the shell tab: the bar stands before the next tab and the release makes the
            // pane a tab of its own there, which the checks below take as the move out.
            var canvasOrigin = shellCanvas.TransformToVisual(_tabs).TransformPoint(new Windows.Foundation.Point(0, 0));
            await WaitForAsync(() => Task.FromResult(shellCanvas.Arrangement is { Panes.Count: 2 }));
            var firstRect = shellCanvas.Arrangement!.Panes.Single(placement => placement.Pane == pane.Descriptor.Id).Rect;
            var rightOfFirst = new Windows.Foundation.Point(canvasOrigin.X + firstRect.Right - 10, canvasOrigin.Y + firstRect.Y + firstRect.Height / 2);
            BeginPaneDragForCheck(movingPane);
            TrackPaneDragForCheck(rightOfFirst);
            var resplitMarked = shellCanvas.DropShown is { Allowed: true } resplitMark && resplitMark.Label == movingPane.DisplayName && StripBarIndex is null;
            var resplitMoved = EndPaneDragForCheck(rightOfFirst) && shellHost.Panes.SequenceEqual([pane, movingPane]) && shellHost.Saved.Layout is { } resplitTree
                && PaneLayout.DirectionOf(resplitTree) == PaneSplit.Columns && PanesTiled(shellHost) && _activePane == movingPane && shellCanvas.DropShown is null;
            await WaitForAsync(() => Task.FromResult(shellCanvas.Arrangement is { Panes.Count: 2 } && shellCanvas.Arrangement.Panes[1].Rect.X > 0));
            var movedRect = shellCanvas.Arrangement!.Panes.Single(placement => placement.Pane == movingPane.Descriptor.Id).Rect;
            var onItself = new Windows.Foundation.Point(canvasOrigin.X + movedRect.X + movedRect.Width / 2, canvasOrigin.Y + movedRect.Y + movedRect.Height / 2);
            BeginPaneDragForCheck(movingPane);
            TrackPaneDragForCheck(onItself);
            var selfRefused = shellCanvas.DropShown is { Allowed: false, Label: "" } && !EndPaneDragForCheck(onItself) && shellHost.Panes.Count == 2 && shellCanvas.DropShown is null;
            var shellOrigin = shellHost.Item.TransformToVisual(_tabs).TransformPoint(new Windows.Foundation.Point(0, 0));
            var afterShell = new Windows.Foundation.Point(shellOrigin.X + shellHost.Item.ActualWidth + 2, shellOrigin.Y + shellHost.Item.ActualHeight / 2);
            var shellIndex = _tabs.TabItems.IndexOf(shellHost.Item);
            BeginPaneDragForCheck(movingPane);
            TrackPaneDragForCheck(afterShell);
            var barShown = StripBarIndex == shellIndex + 1 && shellCanvas.DropShown is null;
            var stripDropped = EndPaneDragForCheck(afterShell) && StripBarIndex is null;
            var outHost = HostOf(movingPane) ?? throw new InvalidOperationException("The pane dropped on the strip has no tab.");
            await WaitForAsync(() => Task.FromResult(SelectedHost == outHost && outHost.Content.ActualWidth > 0 && movingPane.ActualHeight > 0));
            // A pane that is its tab's only one has no strip target: no bar, and nothing moves.
            BeginPaneDragForCheck(pane);
            TrackPaneDragForCheck(afterShell);
            var soleRefused = StripBarIndex is null && !EndPaneDragForCheck(afterShell) && HostOf(pane) == shellHost;
            _tabs.SelectedItem = outHost.Item;
            var paneDragged = resplitMarked && resplitMoved && selfRefused && barShown && stripDropped && soleRefused;
            if (!paneDragged) throw new InvalidOperationException($"Dragging a pane by its header did not re-split within its tab, refuse itself, or make a tab on the strip: marked={resplitMarked} resplit={resplitMoved} self={selfRefused} bar={barShown} strip={stripDropped} sole={soleRefused}.");
            var outNonce = Guid.NewGuid().ToString("N");
            await movingPane.Terminal!.SendFromRendererAsync("PING:" + outNonce + "\r");
            await WaitForAsync(async () => (await movingPane.Terminal!.ReadScreenAsync()).Contains("PONG:" + outNonce));
            var movedOut = movedOutOffered && outHost.Panes.SequenceEqual([movingPane]) && _tabs.TabItems.IndexOf(outHost.Item) == _tabs.TabItems.IndexOf(shellHost.Item) + 1
                && shellHost.Panes.SequenceEqual([pane]) && shellHost.Content.Dividers.Count == 0 && PanesTiled(shellHost) && PanesTiled(outHost) && !pane.CanMoveOut && !movingPane.CanMoveOut
                && ReferenceEquals(movingPane.HeaderFill, outHost.Paint.Selected) && _activePane == movingPane && MovePaneToNewTab(pane) is null
                && outHost.Saved.CustomTitle is null && outHost.Saved.Color is null && outHost.Title.Text == "moving tab"
                && SavedLayout(() => new WorkspaceLayout()).Tabs is { } tabsAfterOut && tabsAfterOut.Any(tab => tab.Id == outHost.Saved.Id && tab.Panes.Count == 1 && tab.Panes[0].Id == movingPane.Descriptor.Id)
                && tabsAfterOut.Any(tab => tab.Id == shellHost.Saved.Id && tab.Panes.Count == 1);
            if (!movedOut) throw new InvalidOperationException($"Move pane to new tab did not give the pane a tab of its own after its old tab with its terminal answering: offered={movedOutOffered} tabs={_tabs.TabItems.Count} shellPanes={shellHost.Panes.Count} outPanes={outHost.Panes.Count}.");
            // A pane out of a tab of several into another tab, left of that tab's only pane: the target, called after that pane,
            // keeps the name as its own; the home tab is retiled to its first pane alone.
            var extraPane = AddPane(shellHost, new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · extra", data, ""), pane, PaneSplit.Rows);
            MovePaneInto(extraPane, outHost, movingPane.Descriptor.Id, PaneDirection.Left);
            var crossMoved = outHost.Panes.SequenceEqual([extraPane, movingPane]) && shellHost.Panes.SequenceEqual([pane]) && SelectedHost == outHost && _activePane == extraPane
                && outHost.Saved.CustomTitle == "PowerShell · moving tab" && outHost.Title.Text == "moving tab" && outHost.Saved.Layout is { } crossTree && PaneLayout.DirectionOf(crossTree) == PaneSplit.Columns
                && ReferenceEquals(extraPane.HeaderFill, outHost.Paint.Selected) && PanesTiled(outHost) && PanesTiled(shellHost) && !_open.Values.Any(host => host.Panes.Count(item => item == extraPane) > 1);
            if (!crossMoved) throw new InvalidOperationException($"Moving a pane into another tab did not place it, keep the target's name and retile the home tab: out=[{string.Join(", ", outHost.Panes.Select(item => item.NameText))}] shell={shellHost.Panes.Count} title='{outHost.Title.Text}'.");
            await movingPane.Terminal.SendFromRendererAsync("EXIT\r");
            await movingPane.Terminal.StopAsync();
            await CloseTabAsync(outHost.Item);
            _tabs.SelectedItem = shellHost.Item;

            // Consolidate tabs by folder (MainWindow.Consolidate.cs): five tabs in two folders of their own, one of them running a
            // fixture shell, become two: each joins the first tab of its folder in strip order, moved whole onto the pane with the
            // most room, the running one still answering from its new tab and painted as that tab; the tabs are tiled, saved, the
            // tab showing stays showing, the rail's button goes dark once no two tabs share a folder, and a second run does nothing.
            var folderA = Path.Combine(data, "consolidate-a"); var folderB = Path.Combine(data, "consolidate-b");
            Directory.CreateDirectory(folderA); Directory.CreateDirectory(folderB);
            TabHost FolderTab(string name, string folder) => CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "PowerShell · " + name, [new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · " + name, folder, "")]), select: false);
            var tabA1 = FolderTab("a1", folderA); var tabB1 = FolderTab("b1", folderB); var tabA2 = FolderTab("a2", folderA);
            var paneA2 = tabA2.Panes[0];
            _tabs.SelectedItem = tabA2.Item;
            await paneA2.StartAsync(CreateShellRequest(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", SmokeScript], folderA), SmokeStart);
            await WaitForAsync(async () => (await paneA2.Terminal!.ReadScreenAsync()).Contains("READY"));
            var tabA3 = FolderTab("a3", folderA); var tabB2 = FolderTab("b2", folderB);
            _tabs.SelectedItem = shellHost.Item;
            var consolidateOffered = !_railItems.ContainsKey("consolidate") && shellHost.Item.ContextFlyout is MenuFlyout consolidateMenu
                && consolidateMenu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Consolidate tabs by folder");
            var tabsBeforeConsolidate = _open.Count;
            // Among the five only: tabs the earlier checks left open share the data folder with the shell tab, and the button acts on them all.
            TabHost[] consolidating = [tabA1, tabB1, tabA2, tabA3, tabB2];
            var consolidation = ConsolidateTabs(consolidating);
            var consolidateNonce = Guid.NewGuid().ToString("N");
            await paneA2.Terminal!.SendFromRendererAsync("PING:" + consolidateNonce + "\r");
            await WaitForAsync(async () => (await paneA2.Terminal!.ReadScreenAsync()).Contains("PONG:" + consolidateNonce));
            var consolidatedShape = consolidation.TabsMerged == 3 && consolidation.Groups == 2 && consolidation.LeftOut.Count == 0 && _open.Count == tabsBeforeConsolidate - 3
                && tabA1.Panes.Count == 3 && tabA1.Panes[0].NameText == "a1" && tabA1.Panes.Select(item => item.NameText).ToHashSet().SetEquals(["a1", "a2", "a3"]) && tabA1.Panes.Contains(paneA2)
                && tabB1.Panes.Select(item => item.NameText).SequenceEqual(["b1", "b2"]) && !_open.ContainsKey(tabA2.Item) && !_open.ContainsKey(tabA3.Item) && !_open.ContainsKey(tabB2.Item)
                && paneA2.IsRunning && ReferenceEquals(paneA2.HeaderFill, tabA1.Paint.Selected) && SelectedHost == shellHost && shellHost.Panes.Count == 1
                && ConsolidateTabs([tabA1, tabB1]) is { TabsMerged: 0, Groups: 0 };
            _tabs.SelectedItem = tabA1.Item;
            await WaitForAsync(() => Task.FromResult(SelectedHost == tabA1 && tabA1.Content.ActualWidth > 0 && paneA2.ActualHeight > 0));
            var consolidatedTiled = PanesTiled(tabA1) && tabA1.Content.Dividers.Count == 2;
            var consolidatedSaved = SavedLayout(() => new WorkspaceLayout()).Tabs is { } tabsAfterConsolidate
                && tabsAfterConsolidate.Any(tab => tab.Id == tabA1.Saved.Id && tab.Panes.Count == 3) && tabsAfterConsolidate.Any(tab => tab.Id == tabB1.Saved.Id && tab.Panes.Count == 2)
                && tabsAfterConsolidate.All(tab => tab.Id != tabA2.Saved.Id && tab.Id != tabA3.Saved.Id && tab.Id != tabB2.Saved.Id);
            var tabsConsolidated = consolidateOffered && consolidatedShape && consolidatedTiled && consolidatedSaved;
            if (!tabsConsolidated)
                throw new InvalidOperationException($"Consolidating tabs by folder did not join the tabs of each folder into its first with the terminal answering: offered={consolidateOffered} shape={consolidatedShape} tiled={consolidatedTiled} saved={consolidatedSaved} merged={consolidation.TabsMerged} groups={consolidation.Groups} leftOut=[{string.Join("; ", consolidation.LeftOut)}] a1=[{string.Join(", ", tabA1.Panes.Select(item => item.NameText))}].");
            await paneA2.Terminal.SendFromRendererAsync("EXIT\r");
            await paneA2.Terminal.StopAsync();
            await CloseTabAsync(tabA1.Item);
            await CloseTabAsync(tabB1.Item);
            _tabs.SelectedItem = shellHost.Item;
            var namedSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · named by pane", data, "");
            var namedHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), namedSaved.Title, [namedSaved]));
            var leadingSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · leading", data, "");
            var leadingHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), leadingSaved.Title, [leadingSaved]));
            var leadingPane = leadingHost.Panes[0];
            _tabs.SelectedItem = namedHost.Item;
            await WaitForAsync(() => Task.FromResult(SelectedHost == namedHost && namedHost.Content.ActualWidth > 0 && namedHost.Content.Arrangement is { Panes.Count: 1 }));
            var leftOfFirst = namedHost.Content.DropAt(new Windows.Foundation.Point(10, namedHost.Content.ActualHeight / 2))!;
            MoveTabInto(leadingHost, namedHost, leftOfFirst.Pane, leftOfFirst.Side);
            var tabKeptName = leftOfFirst.Side == PaneDirection.Left && namedHost.Panes.Count == 2 && namedHost.Panes[0] == leadingPane && !_open.ContainsKey(leadingHost.Item)
                && namedHost.Saved.CustomTitle == "PowerShell · named by pane" && namedHost.Title.Text == "named by pane" && namedHost.Panes[0].NameText == "leading"
                && namedHost.Saved.Layout is { } leadTree && PaneLayout.DirectionOf(leadTree) == PaneSplit.Columns && PanesTiled(namedHost);
            await CloseTabAsync(namedHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            var tabMoved = dropSides && dropRefusedIntoItself && markShown && markHidden && hoverShown && hoverReturned && tabMovedIn && movedFocused && movePersisted && tabKeptName
                && shellHost.Panes.Count == 1 && pane.IsRunning;
            if (!tabMoved)
                throw new InvalidOperationException($"Dropping a tab into another did not land it where the drop said, show the mark, show a hovered tab, move the panes with their terminals answering, save, or keep the target's name: sides={dropSides} refused={dropRefusedIntoItself} mark={markShown}/{markHidden} hover={hoverShown}/{hoverReturned} moved={tabMovedIn} focused={movedFocused} saved={movePersisted} name={tabKeptName} panes={shellHost.Panes.Count}.");

            // Workspaces (docs/decisions/workspaces.md): the folder decides. A folder of the data folder added to Folders takes the running
            // tab working in it out of the first workspace without showing it, the first staying current with the indicator up; the new
            // workspace's row lists the tab, folded, while the first's is open; switching swaps the strip whole and the moved terminal
            // still answers; the layout is saved with both workspaces and the current one's tabs beside them; the shortcuts by digit and
            // by step; a tab opened in the folder lands there with the strip following (PlaceTab); a tab outside every listed folder
            // brings Scratch, last, which goes with its tab; the indicator's list names them all; and removing the folder sends its
            // tab back to the folder above, the strip following.
            var wsParts = new Dictionary<string, bool>();
            void Part(string name, bool ok) => wsParts[name] = ok;
            var wsPanelBefore = _preferences.ActivePanel; var wsPanelOpenBefore = _panelOpen;
            var wsFolder = PinnedFolders.Normalize(Path.Combine(data, "grouped"));
            Directory.CreateDirectory(wsFolder);
            var wsGroupedSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · grouped", wsFolder, "");
            var wsGroupedHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), wsGroupedSaved.Title, [wsGroupedSaved]));
            var wsGroupedPane = wsGroupedHost.Panes[0];
            await wsGroupedPane.StartAsync(CreateShellRequest(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", SmokeScript], wsFolder), SmokeStart);
            await WaitForAsync(async () => (await wsGroupedPane.Terminal!.ReadScreenAsync()).Contains("READY"));
            var wsFirstWorkspace = _workspace;
            var wsStripBefore = _tabs.TabItems.Count;
            Part("rule.before", ReferenceEquals(WorkspaceOf(wsGroupedHost), wsFirstWorkspace) && RootFor(wsFolder) == PinnedFolders.Normalize(data) && ReferenceEquals(WorkspaceFor(wsFolder), wsFirstWorkspace));
            var wsSecond = AddFolder(wsFolder + "\\");
            Part("added.list", Folders.SequenceEqual([PinnedFolders.Normalize(data), wsFolder]) && SavedPreferences(() => new AppPreferences()).Folders is { Count: 2 } savedFolders && PinnedFolders.Same(savedFolders[1], wsFolder));
            Part("added.two", _workspaces.Count == 2 && ReferenceEquals(_workspace, wsFirstWorkspace) && ReferenceEquals(_workspaces[1], wsSecond) && wsSecond.Id == wsFolder && wsSecond.Home == wsFolder && WorkspaceName(wsSecond) == "grouped");
            Part("added.chip", _workspaceChip.Visibility == Visibility.Visible && _workspaceChipText.Text == Paths.Leaf(data));
            Part("movedOut", _tabs.TabItems.Count == wsStripBefore - 1 && !_tabs.TabItems.Contains(wsGroupedHost.Item) && _open.ContainsKey(wsGroupedHost.Item) && wsGroupedPane.IsRunning
                && HostsOf(wsSecond).SequenceEqual([wsGroupedHost]) && ReferenceEquals(WorkspaceOf(wsGroupedHost), wsSecond) && wsSecond.SelectedTabId == wsGroupedHost.Saved.Id && SelectedHost != wsGroupedHost);
            OpenPanel("workspaces");
            await WaitForAsync(() => Task.FromResult(_workspaceRows.Count == 2 && _workspaceRows[wsSecond.Id].Row.ActualWidth > 0));
            Part("rows.first", _workspaceRows[wsFirstWorkspace.Id].Tabs.Visibility == Visibility.Visible && _workspaceRows[wsFirstWorkspace.Id].TabRows.Count == wsStripBefore - 1 && _workspaceRows[wsFirstWorkspace.Id].Bar.Visibility == Visibility.Visible);
            // Every pane after a tab's first has a row of its own under its tab's.
            Part("rows.panes", _workspaceRows[wsFirstWorkspace.Id].PaneRows.Count == HostsOf(wsFirstWorkspace).Sum(host => host.Panes.Count - 1) && _workspaceRows[wsFirstWorkspace.Id].Guide.Visibility == Visibility.Visible && _workspaceRows[wsSecond.Id].PaneRows.Count == 0);
            // Both rows are open to their sessions: only a chevron folds one.
            Part("rows.second", _workspaceRows[wsSecond.Id].Tabs.Visibility == Visibility.Visible && _workspaceRows[wsSecond.Id].TabRows.Count == 1 && _workspaceRows[wsSecond.Id].Bar.Visibility == Visibility.Collapsed);
            var wsSummaryNote = _workspaceRows[wsSecond.Id].Summary.Text + " | glyph " + _workspaceRows[wsSecond.Id].Glyph.Visibility + " | home " + wsSecond.Home;
            Part("rows.summary", _workspaceRows[wsSecond.Id].Summary.Text == "1 tab · " + wsFolder && _workspaceRows[wsSecond.Id].Glyph.Visibility == Visibility.Visible);
            SwitchWorkspace(wsSecond);
            await WaitForAsync(() => Task.FromResult(SelectedHost == wsGroupedHost && wsGroupedHost.Content.ActualWidth > 0 && wsGroupedPane.Terminal is not null));
            var wsGroupedNonce = Guid.NewGuid().ToString("N");
            await wsGroupedPane.Terminal!.SendFromRendererAsync("PING:" + wsGroupedNonce + "\r");
            await WaitForAsync(async () => (await wsGroupedPane.Terminal!.ReadScreenAsync()).Contains("PONG:" + wsGroupedNonce));
            Part("switched.strip", ReferenceEquals(_workspace, wsSecond) && _tabs.TabItems.Count == 1 && wsFirstWorkspace.Parked.Count == wsStripBefore - 1 && _activePane == wsGroupedPane);
            // The switch folded nothing; the first workspace's chevron folds its row, which stays folded through a switch there and back and is saved, and opens it again.
            Part("switched.rows", _workspaceRows[wsSecond.Id].Tabs.Visibility == Visibility.Visible && _workspaceRows[wsFirstWorkspace.Id].Tabs.Visibility == Visibility.Visible && _workspaceRows[wsFirstWorkspace.Id].Bar.Visibility == Visibility.Collapsed);
            new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(_workspaceRows[wsFirstWorkspace.Id].Fold).Invoke();
            var wsFolded = await EventuallyAsync(() => !WorkspaceExpanded(wsFirstWorkspace) && _workspaceRows[wsFirstWorkspace.Id].Tabs.Visibility == Visibility.Collapsed && _workspaceRows[wsFirstWorkspace.Id].Summary.Visibility == Visibility.Visible)
                && SavedPreferences(() => new AppPreferences()).FoldedSections.Contains(SectionFolds.Tabs(wsFirstWorkspace.Id)) && _workspaceRows[wsSecond.Id].Tabs.Visibility == Visibility.Visible;
            SwitchWorkspace(wsFirstWorkspace); SwitchWorkspace(wsSecond);
            var wsStayedFolded = !WorkspaceExpanded(wsFirstWorkspace) && _workspaceRows[wsFirstWorkspace.Id].Tabs.Visibility == Visibility.Collapsed && _workspaceRows[wsSecond.Id].Tabs.Visibility == Visibility.Visible;
            new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(_workspaceRows[wsFirstWorkspace.Id].Fold).Invoke();
            Part("folded", wsFolded && wsStayedFolded && await EventuallyAsync(() => WorkspaceExpanded(wsFirstWorkspace) && _workspaceRows[wsFirstWorkspace.Id].Tabs.Visibility == Visibility.Visible));
            Part("switched.status", _statusText.Text.StartsWith("grouped · ", StringComparison.Ordinal) && _workspaceChipText.Text == "grouped" && NewSessionFolder == wsFolder);
            var wsSavedLayout = SavedLayout(() => new WorkspaceLayout());
            Part("saved.list", wsSavedLayout.Workspaces is { Count: 2 } && wsSavedLayout.SelectedWorkspaceId == wsSecond.Id && wsSavedLayout.Workspaces[1].Id == wsSecond.Id && wsSavedLayout.Workspaces[1].Home == wsFolder);
            Part("saved.second", wsSavedLayout.Workspaces is { Count: 2 } wsSavedList && wsSavedList[1].Tabs.Select(tab => tab.Id).SequenceEqual([wsGroupedHost.Saved.Id]) && wsSavedList[0].Tabs.Count == wsStripBefore - 1);
            Part("saved.tabs", wsSavedLayout.Tabs.Select(tab => tab.Id).SequenceEqual([wsGroupedHost.Saved.Id]) && wsSavedLayout.SelectedTabId == wsGroupedHost.Saved.Id);
            await HandleShortcutAsync("workspace-1");
            Part("digit", ReferenceEquals(_workspace, wsFirstWorkspace) && _tabs.TabItems.Count == wsStripBefore - 1 && wsSecond.Parked.SequenceEqual([wsGroupedHost.Item]) && _activePane != wsGroupedPane);
            await HandleShortcutAsync("next-workspace");
            Part("step", ReferenceEquals(_workspace, wsSecond));
            await HandleShortcutAsync("previous-workspace");
            Part("stepBack", ReferenceEquals(_workspace, wsFirstWorkspace));
            // A tab opened under the folder while the first workspace shows: made in the strip, the rule moves it to the folder's workspace and the strip follows.
            var wsPlacedHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "PowerShell · placed", [new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · placed", Path.Combine(wsFolder, "deeper"), "")]));
            var wsPlaced = PlaceTab(wsPlacedHost);
            Part("placed", ReferenceEquals(wsPlaced, wsSecond) && ReferenceEquals(_workspace, wsSecond) && HostsOf(wsSecond).SequenceEqual([wsGroupedHost, wsPlacedHost]) && SelectedHost == wsPlacedHost);
            await CloseTabAsync(wsPlacedHost.Item);
            // A tab outside every listed folder: Scratch appears, last, holds it, is current, and goes once the tab closes and the strip has left it.
            var wsStrayFolder = Path.GetTempPath();
            var wsStrayHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "PowerShell · stray", [new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · stray", wsStrayFolder, "")]));
            var wsScratch = PlaceTab(wsStrayHost);
            Part("scratch.made", wsScratch.IsScratch && wsScratch.Id == Workspaces.ScratchId && _workspaces.Count == 3 && ReferenceEquals(_workspaces[2], wsScratch) && ReferenceEquals(_workspace, wsScratch) && WorkspaceName(wsScratch) == Workspaces.ScratchName
                && RootFor(wsStrayFolder) is null && _workspaceChipText.Text == Workspaces.ScratchName && _workspaceChipGlyph.Glyph == ScratchGlyph && HostsOf(wsScratch).SequenceEqual([wsStrayHost]));
            var wsListClosed = false;
            void WsListClosed(object? sender, object args) => wsListClosed = true;
            _workspaceChip.Flyout.Closed += WsListClosed;
            var wsSwitcher = WorkspaceSwitcherItems();
            var wsSwitcherRows = wsSwitcher.OfType<MenuFlyoutItem>().ToList();
            Part("switcher", wsSwitcherRows.Select(item => item.Text).SequenceEqual([Paths.Leaf(data), "grouped", Workspaces.ScratchName]) && wsSwitcherRows.Count(item => item.FontWeight.Weight == 700) == 1 && wsSwitcherRows[^1].FontWeight.Weight == 700
                && wsSwitcherRows.All(item => item.Icon is FontIcon) && wsSwitcherRows[1].KeyboardAcceleratorTextOverride == "1 tab" && wsSwitcher.Count == wsSwitcherRows.Count && _workspaceChip.Flyout is MenuFlyout && _workspaceChip.ContextFlyout is null);
            // A list still closing ignores a ShowAt, so the key waits for the read above to finish closing it.
            await EventuallyAsync(() => wsListClosed);
            _workspaceChip.Flyout.Closed -= WsListClosed;
            var wsListOpenings = 0;
            void WsListOpening(object? sender, object args) => wsListOpenings++;
            _workspaceChip.Flyout.Opening += WsListOpening;
            await HandleShortcutAsync("workspace-list");
            var wsListShown = await EventuallyAsync(() => _workspaceChip.Visibility == Visibility.Visible ? _workspaceChip.Flyout.IsOpen : _panelOpen && _preferences.ActivePanel == "workspaces");
            var wsListState = $"switcher.key(chip {_workspaceChip.Visibility}, openings {wsListOpenings}, open {_workspaceChip.Flyout.IsOpen}, panel {_panelOpen} {_preferences.ActivePanel})";
            // Each open row ends in its workspace's state mark, in the row, as the tabs stand: a running tab's green dot, or none.
            if (_workspaceChip.Visibility == Visibility.Visible)
            {
                var wsStatesPlaced = await EventuallyAsync(() => SwitcherStates.Count == _workspaces.Count && SwitcherStates.All(state => state.Placed));
                var wsStatesRight = SwitcherStates.Zip(_workspaces).All(pair =>
                {
                    var (state, workspace) = pair;
                    var running = HostsOf(workspace).Any(host => host.Panes.Any(pane => pane.IsRunning));
                    return state.Name == ((MenuFlyoutItem)wsSwitcher[_workspaces.IndexOf(workspace)]).Text && WorkspacePresence(workspace) switch
                    {
                        PaneState.Working => state.Shown == "ring",
                        PaneState.Idle => running ? state.Shown == "dot" && ReferenceEquals(state.Fill, _theme.SuccessDot) : state.Shown == "none",
                        _ => state.Shown == "dot"
                    };
                });
                Part($"switcher.states({string.Join("; ", SwitcherStates.Select(state => $"{state.Name} {state.Placed} {state.Shown}"))})", wsStatesPlaced && wsStatesRight);
            }
            // While the list is open the indicator's pill stays lit and its chevron points up.
            if (_workspaceChip.Visibility == Visibility.Visible && _workspaceChip.Flyout.IsOpen) Part("switcher.chevron", await EventuallyAsync(() => WorkspaceChipFace is { ChevronUp: true } face && ReferenceEquals(face.Fill, _chipHover)));
            _workspaceChip.Flyout.Opening -= WsListOpening;
            _workspaceChip.Flyout.Hide();
            Part(wsListState, wsListShown);
            SwitchWorkspace(wsSecond);
            Part("scratch.kept", _workspaces.Count == 3 && ReferenceEquals(_workspace, wsSecond) && Scratch is not null);
            await CloseTabAsync(wsStrayHost.Item);
            Part("scratch.gone", await EventuallyAsync(() => Scratch is null && _workspaces.Count == 2 && !_workspaceRows.ContainsKey(Workspaces.ScratchId)));
            // The folder removed: its tab goes back to the folder above, the strip following; the list, the rows and the saved layout are without it.
            RemoveFolder(wsFolder);
            Part("removed.list", Folders.SequenceEqual([PinnedFolders.Normalize(data)]) && _workspaces.Count == 1 && ReferenceEquals(_workspace, wsFirstWorkspace) && _workspaceRows.Count == 1 && !IsFolder(wsFolder));
            Part("removed.tabs", _tabs.TabItems.Contains(wsGroupedHost.Item) && _open.ContainsKey(wsGroupedHost.Item) && wsGroupedPane.IsRunning && SelectedHost == wsGroupedHost && _tabs.TabItems.Count == wsStripBefore);
            Part("removed.saved", SavedLayout(() => new WorkspaceLayout()).Workspaces is { Count: 1 } && SavedPreferences(() => new AppPreferences()).Folders is { Count: 1 } && LastNotice is { Brief: true, Title: "Removed from Folders" });
            await CloseTabAsync(wsGroupedHost.Item);
            if (wsPanelOpenBefore) OpenPanel(wsPanelBefore); else await SetPanelOpen(false);
            var workspacesGrouped = wsParts.Values.All(ok => ok);
            if (!workspacesGrouped)
                throw new InvalidOperationException("Workspaces did not follow the folders as they should; failed: " + string.Join(", ", wsParts.Where(part => !part.Value).Select(part => part.Key))
                    + $" (strip before={wsStripBefore}, now={_tabs.TabItems.Count}, workspaces={_workspaces.Count}, folders=[{string.Join("; ", Folders)}], summary='{wsSummaryNote}').");

            // Two requests to resume one pane through the ordinary path, the second made while the first still waits for its
            // terminal page (a double-click on Resume). The second waits for the first instead of making a start of its own:
            // one terminal, which starts, and no notice (it used to be refused with one, and before that to dispose the first
            // request's terminal). A start that reaches the running pane some other way is still refused; the log names its process.
            var conflictSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · start conflict", data, "");
            var conflictPane = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), conflictSaved.Title, [conflictSaved])).Panes[0];
            _notice.IsOpen = false;
            var firstStart = ResumePaneAsync(conflictPane, new("the verification's first start", DateTimeOffset.UtcNow));
            var startingTerminal = conflictPane.Terminal;
            var heldFromRequest = conflictPane.Resuming;
            var secondStart = ResumePaneAsync(conflictPane, new("the verification's second start", DateTimeOffset.UtcNow));
            var secondWaited = !secondStart.IsCompleted;
            await Task.WhenAll(firstStart, secondStart);
            var joinNotice = _notice.IsOpen ? _notice.Message ?? "" : "";
            await WaitForAsync(async () => (await conflictPane.Terminal!.ReadScreenAsync()).Contains("READY"));
            var thirdStart = new StartRequest("the verification's third start", DateTimeOffset.UtcNow);
            PaneStartedException? runningConflict = null;
            try { await conflictPane.StartAsync(CreateShellRequest(SmokeShellArguments, data), thirdStart); }
            catch (PaneStartedException started) { runningConflict = started; }
            var runningConflictText = runningConflict is null ? null : StartConflictOf(conflictPane, thirdStart, runningConflict, null, null);
            var runningNotice = runningConflictText?.Summary ?? "";
            var runningLogged = runningConflictText?.Describe() ?? "";
            var resumeJoined = startingTerminal is not null && heldFromRequest && secondWaited && joinNotice.Length == 0
                && ReferenceEquals(conflictPane.Terminal, startingTerminal) && conflictPane.IsRunning && !conflictPane.Resuming
                && runningConflict is { Running: true, ProcessId: { } runningProcess } && runningProcess == conflictPane.Terminal!.ProcessId
                && runningNotice == "This pane is already running; the second start was skipped." && runningLogged.Contains($"has run as process {runningProcess} for ", StringComparison.Ordinal);
            _notice.IsOpen = false;
            await conflictPane.Terminal!.SendFromRendererAsync("EXIT\r");
            await conflictPane.Terminal.StopAsync();
            await CloseTabAsync(HostOf(conflictPane)!.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!resumeJoined) throw new InvalidOperationException($"A second resume of one pane did not wait for the first without a notice, or a further start of the running pane was not refused with its process named in the log: held={heldFromRequest} waited={secondWaited} notice='{joinNotice}' running='{runningNotice}' logged='{runningLogged}'.");

            // A start that fails before anything runs (here an executable that does not exist) takes its empty terminal away
            // and puts the placeholder back with the error and its button, and the pane starts on the next request.
            var failSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · failed start", data, "");
            var failPane = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), failSaved.Title, [failSaved])).Panes[0];
            Exception? failure = null;
            try { await failPane.StartAsync(new TerminalLaunchRequest(Path.Combine(data, "missing-" + Guid.NewGuid().ToString("N") + ".exe"), [], data), new("the verification's failing start", DateTimeOffset.UtcNow)); }
            catch (Exception exception) { failure = exception; }
            var failedCleanly = failure is not null && failPane.Terminal is null && failPane.Status == PaneStatus.Failed && failPane.CurrentStart is null
                && failPane.PreviewWarning is { } warning && warning.StartsWith("The terminal did not start: ", StringComparison.Ordinal) && warning.Contains(failure.Message, StringComparison.Ordinal);
            await ResumePaneAsync(failPane, new("the verification's retry", DateTimeOffset.UtcNow));
            await WaitForAsync(async () => failPane.Terminal is { } retried && (await retried.ReadScreenAsync()).Contains("READY"));
            var failedStartRecovered = failedCleanly && failPane.IsRunning && failPane.Status == PaneStatus.Running;
            await failPane.Terminal!.SendFromRendererAsync("EXIT\r");
            await failPane.Terminal.StopAsync();
            await CloseTabAsync(HostOf(failPane)!.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!failedStartRecovered) throw new InvalidOperationException($"A failed start did not put the placeholder back with its error, or the retry did not start: failure='{failure?.Message}' warning='{failPane.PreviewWarning}' status={failPane.Status}.");

            // A conversation whose folder the history never recorded. With no folder known, the Sessions card's Resume is off and
            // a resume from the pane says what to do. Once SIC keeps one, the card offers Resume and the pane's resume takes the
            // folder before anything else: here it then stops at the history check, since the session does not exist, and nothing
            // starts. Adopting an identity keeps the pane's folder for the conversation.
            var claudeHome = _preferences.Agents.Single(agent => agent.Id == "claude").HistoryDirectory;
            var folderless = new Conversation("claude", Guid.NewGuid().ToString(), "Folder unknown fixture", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "", claudeHome, "");
            var folderlessHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), folderless.Title, [new SavedPane(Guid.NewGuid().ToString("N"), "claude", folderless.Title, "", claudeHome, folderless.SessionId)]));
            var folderlessPane = folderlessHost.Panes[0];
            RefreshPreviewState(folderless);
            var cardOffWithoutFolder = !_previewResume.IsEnabled && FolderOf(folderless) is null;
            _notice.IsOpen = false;
            await ResumePaneAsync(folderlessPane, new("the verification's resume without a folder", DateTimeOffset.UtcNow));
            var unknownNotice = _notice.IsOpen ? _notice.Message ?? "" : "";
            _preferences.ConversationFolders[folderless.Key] = data;
            RefreshPreviewState(folderless);
            var cardOnWithKeptFolder = _previewResume.IsEnabled && FolderOf(folderless) == data;
            _notice.IsOpen = false;
            await ResumePaneAsync(folderlessPane, new("the verification's resume with a kept folder", DateTimeOffset.UtcNow));
            var keptNotice = _notice.IsOpen ? _notice.Message ?? "" : "";
            var folderHealed = folderlessPane.Descriptor.WorkingDirectory == data && !folderlessPane.IsRunning && !keptNotice.Contains("folder", StringComparison.OrdinalIgnoreCase);
            _preferences.ConversationFolders.Remove(folderless.Key);
            var adoptedKey = "claude:" + Guid.NewGuid();
            AdoptIdentity(folderlessHost, folderlessPane, adoptedKey);
            var folderAdopted = _preferences.ConversationFolders.GetValueOrDefault(adoptedKey) == data;
            _preferences.ConversationFolders.Remove(adoptedKey);
            SavePreferences(); FlushState();
            _notice.IsOpen = false;
            await CloseTabAsync(folderlessHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            var unknownFolderHandled = cardOffWithoutFolder && unknownNotice.Contains("The folder this conversation ran in is unknown.", StringComparison.Ordinal)
                && unknownNotice.Contains("Link saved conversation", StringComparison.Ordinal) && cardOnWithKeptFolder && folderHealed && folderAdopted;
            if (!unknownFolderHandled) throw new InvalidOperationException($"A conversation without a recorded folder was not handled: card off={cardOffWithoutFolder} notice='{unknownNotice}' card on with kept folder={cardOnWithKeptFolder} healed={folderHealed} ('{folderlessPane.Descriptor.WorkingDirectory}', notice '{keptNotice}') adopted={folderAdopted}.");

            // A resume whose tab is not showing when its terminal is made (the user moved to another tab while the saved
            // history was checked). TabView holds only the selected tab's content, so the view is not loaded and its page
            // cannot start; the start used to time out 25 s after its terminal was made. It now waits for the tab. The load budget
            // is cut to a second during the wait, so a budget that still ran from then would fail inside it.
            var backgroundSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · background start", data, "");
            var backgroundHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), backgroundSaved.Title, [backgroundSaved]), select: false);
            var backgroundPane = backgroundHost.Panes[0];
            _notice.IsOpen = false;
            TerminalView.LoadBudget = TimeSpan.FromSeconds(1);
            var backgroundStart = ResumePaneAsync(backgroundPane, new("the verification's start in a background tab", DateTimeOffset.UtcNow));
            await Task.Delay(TimeSpan.FromSeconds(3));
            var waitedForTab = !backgroundStart.IsCompleted && !_notice.IsOpen && backgroundPane.Status == PaneStatus.Starting && backgroundPane.Resuming;
            TerminalView.LoadBudget = TimeSpan.FromSeconds(25);
            if (!waitedForTab) throw new InvalidOperationException($"A start in a background tab did not wait for its tab: done={backgroundStart.IsCompleted} status={backgroundPane.Status} notice='{(_notice.IsOpen ? _notice.Message : "")}'.");
            _tabs.SelectedItem = backgroundHost.Item;
            await backgroundStart;
            await WaitForAsync(async () => backgroundPane.Terminal is { } shown && (await shown.ReadScreenAsync()).Contains("READY"));
            var backgroundStartWaited = waitedForTab && backgroundPane.IsRunning && !_notice.IsOpen;
            await backgroundPane.Terminal!.SendFromRendererAsync("EXIT\r");
            await backgroundPane.Terminal.StopAsync();
            await CloseTabAsync(backgroundHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!backgroundStartWaited) throw new InvalidOperationException($"A start in a background tab did not wait for its tab, or did not start once it was shown: waited={waitedForTab} status={backgroundPane.Status} notice='{(_notice.IsOpen ? _notice.Message : "")}'.");

            // Resume all. Placeholders in tabs that are not selected (two tabs, one holding two panes), one whose folder is missing,
            // and an agent pane with no saved conversation. Each tab with something to start is shown in turn, since a terminal
            // loads only while its tab is showing; the unlinked tab is not; the tab that was selected is selected again at the
            // end; the three shells run; a second request during the run joins it; and the missing folder and the unlinked pane
            // are reported in one warning notice with no notice per pane.
            // A tab without a name of its own is called after its first pane, so a tab of one pane gives that pane the title the checks read.
            TabHost PlaceholderTab(string title, string folder, params string[] agents) =>
                CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), title, agents.Select((agent, index) => new SavedPane(Guid.NewGuid().ToString("N"), agent, agents.Length == 1 ? title : title + " " + (index + 1), folder, "")).ToList()), select: false);
            // The fixture's Claude Code placeholder is closed for this and reopened after: it is linked to a saved conversation, so
            // Resume all would launch the real CLI for it, and the smoke makes no such launch.
            await CloseTabAsync(_open.Values.Single(open => open.Panes.Any(item => item.Descriptor.SessionId == SmokeSessionId)).Item);
            var resumeAllA = PlaceholderTab("PowerShell · resume all A", data, "shell");
            var resumeAllB = PlaceholderTab("PowerShell · resume all B", data, "shell", "shell");
            var missingFolder = Path.Combine(data, "missing-" + Guid.NewGuid().ToString("N"));
            var resumeAllMissing = PlaceholderTab("PowerShell · resume all missing folder", missingFolder, "shell");
            var resumeAllUnlinked = PlaceholderTab("Claude Code · unlinked", data, "claude");
            var shownDuringResumeAll = new HashSet<TabHost>();
            void NoteShown(object sender, SelectionChangedEventArgs args) { if (SelectedHost is { } shown) shownDuringResumeAll.Add(shown); }
            _tabs.SelectionChanged += NoteShown;
            _notice.IsOpen = false;
            var resumeAllRun = ResumeAllAsync(new("the verification's Resume all", DateTimeOffset.UtcNow));
            var resumeAllHeld = ResumingAll && !ResumeAllAsync(new("the verification's second Resume all", DateTimeOffset.UtcNow)).IsCompleted;
            var resumeAllClean = await resumeAllRun;
            _tabs.SelectionChanged -= NoteShown;
            var resumeAllPanes = new[] { resumeAllA.Panes[0], resumeAllB.Panes[0], resumeAllB.Panes[1] };
            foreach (var resumed in resumeAllPanes) await WaitForAsync(async () => resumed.Terminal is { } shown && (await shown.ReadScreenAsync()).Contains("READY"));
            var resumeAllMessage = _notice.IsOpen ? _notice.Message ?? "" : "";
            var resumeAll = !resumeAllClean && resumeAllHeld && !ResumingAll && resumeAllPanes.All(item => item.IsRunning)
                && !resumeAllMissing.Panes[0].IsRunning && resumeAllMissing.Panes[0].Terminal is null && !resumeAllUnlinked.Panes[0].IsRunning && resumeAllUnlinked.Panes[0].Terminal is null
                && ReferenceEquals(_tabs.SelectedItem, shellHost.Item) && shownDuringResumeAll.Contains(resumeAllA) && shownDuringResumeAll.Contains(resumeAllB)
                && shownDuringResumeAll.Contains(resumeAllMissing) && !shownDuringResumeAll.Contains(resumeAllUnlinked)
                && _notice.IsOpen && _notice.Severity == InfoBarSeverity.Warning && _notice.Title == "Resumed 3 of 4 sessions" && _notice.ActionButton is null && LastNotice is { Brief: false }
                && resumeAllMessage.Contains("PowerShell in “PowerShell · resume all missing folder” did not start: ", StringComparison.Ordinal) && resumeAllMessage.Contains(missingFolder, StringComparison.Ordinal)
                && resumeAllMessage.Contains("1 not linked to a saved conversation", StringComparison.Ordinal);
            _notice.IsOpen = false;
            foreach (var resumed in resumeAllPanes) { await resumed.Terminal!.SendFromRendererAsync("EXIT\r"); await resumed.Terminal.StopAsync(); }
            foreach (var closing in new[] { resumeAllA, resumeAllB, resumeAllMissing, resumeAllUnlinked }) await CloseTabAsync(closing.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!resumeAll) throw new InvalidOperationException($"Resume all did not start every placeholder tab by tab, return to the selected tab and report the rest in one notice: clean={resumeAllClean} held={resumeAllHeld} shown=[{string.Join(", ", shownDuringResumeAll.Select(shown => shown.Saved.Title))}] selected={SelectedHost?.Saved.Title} title='{_notice.Title}' message='{resumeAllMessage}'.");

            // A new session closed before its first message, and a saved conversation whose history is gone (FreshStart), with a
            // stand-in for Claude Code that prints what it was started with: the smoke launches no real agent. The unsaved one
            // starts again under its own ID, unasked and with no notice, and the mark is in the layout. The lost one is refused
            // as before, its placeholder then offers Start new session here, and that starts a session in the pane's folder
            // under a new ID, named as a new session is.
            var standIn = Path.Combine(data, "stand-in-claude.cmd");
            File.WriteAllText(standIn, "@echo off\r\necho STAND-IN-CLAUDE %*\r\npause >nul\r\n");
            var claudeAsSet = _preferences.Agents.Single(agent => agent.Id == "claude");
            static string Squashed(string text) => string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
            static IEnumerable<DependencyObject> Within(DependencyObject parent)
            {
                for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
                {
                    var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index);
                    yield return child;
                    foreach (var inner in Within(child)) yield return inner;
                }
            }
            bool Offers(SessionPane pane, string text) => Within(pane).OfType<Button>().Any(button => button.Content as string == text);
            bool unsavedStarted = false, unsavedOffered = false, unsavedInLayout = false, lostRefused = false, lostOffered = false, newHere = false;
            string unsavedScreen = "", lostFailure = "", hereScreen = "";
            _preferences.Agents = _preferences.Agents.Select(agent => agent.Id == "claude" ? agent with { Executable = standIn } : agent).ToList();
            try
            {
                var unsavedId = Guid.NewGuid().ToString();
                var unsavedHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "Claude Code · unsaved", [new SavedPane(Guid.NewGuid().ToString("N"), "claude", "Claude Code · unsaved", data, claudeAsSet.HistoryDirectory, unsavedId, Unsaved: true)]));
                var unsavedPane = unsavedHost.Panes[0];
                unsavedOffered = await EventuallyAsync(() => Offers(unsavedPane, "Start session")) && !Offers(unsavedPane, "Start new session here");
                unsavedInLayout = CurrentLayout().Tabs.SelectMany(tab => tab.Panes).Any(pane => pane.SessionId == unsavedId && pane.Unsaved);
                _notice.IsOpen = false;
                var startedAgain = await ResumePaneAsync(unsavedPane, new("the verification's resume of an unsaved session", DateTimeOffset.UtcNow));
                await WaitForAsync(async () => unsavedPane.Terminal is { } shown && (await shown.ReadScreenAsync()).Contains("STAND-IN-CLAUDE"));
                unsavedScreen = Squashed(await unsavedPane.Terminal!.ReadScreenAsync());
                unsavedStarted = startedAgain && unsavedScreen.Contains("--session-id" + unsavedId, StringComparison.Ordinal) && !unsavedScreen.Contains("--resume", StringComparison.Ordinal)
                    && unsavedPane.Descriptor is { Unsaved: true } && unsavedPane.Descriptor.SessionId == unsavedId && !_notice.IsOpen;
                await unsavedPane.Terminal.StopAsync();
                await CloseTabAsync(unsavedHost.Item);

                var lostId = Guid.NewGuid().ToString();
                var lostHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "Claude Code · lost", [new SavedPane(Guid.NewGuid().ToString("N"), "claude", "Claude Code · lost", data, claudeAsSet.HistoryDirectory, lostId)]));
                var lostPane = lostHost.Panes[0];
                var notOfferedFirst = await EventuallyAsync(() => Offers(lostPane, "Resume conversation")) && !Offers(lostPane, "Start new session here");
                var lostStarted = await ResumePaneAsync(lostPane, new("the verification's resume of a conversation whose history is gone", DateTimeOffset.UtcNow), failure => lostFailure = failure);
                lostRefused = notOfferedFirst && !lostStarted && lostPane.Terminal is null && lostFailure.Contains("The saved conversation was not found", StringComparison.Ordinal) && lostPane.Descriptor.SessionId == lostId;
                lostOffered = await EventuallyAsync(() => Offers(lostPane, "Start new session here")) && lostPane.HistoryMissing is not null;
                await StartNewHereAsync(lostPane);
                await WaitForAsync(async () => lostPane.Terminal is { } shown && (await shown.ReadScreenAsync()).Contains("STAND-IN-CLAUDE"));
                hereScreen = Squashed(await lostPane.Terminal!.ReadScreenAsync());
                newHere = lostPane.Descriptor is { Unsaved: true, SessionId: { } newId } && newId != lostId && hereScreen.Contains("--session-id" + newId, StringComparison.Ordinal)
                    && lostPane.HistoryMissing is null && lostPane.Descriptor.WorkingDirectory == data && lostHost.Saved.Title == "Claude Code · " + Paths.Leaf(data);
                await lostPane.Terminal.StopAsync();
                await CloseTabAsync(lostHost.Item);
            }
            finally { _preferences.Agents = _preferences.Agents.Select(agent => agent.Id == "claude" ? claudeAsSet : agent).ToList(); }
            _tabs.SelectedItem = shellHost.Item;
            if (!unsavedStarted || !unsavedOffered || !unsavedInLayout || !lostRefused || !lostOffered || !newHere)
                throw new InvalidOperationException($"A new session closed before its first message did not start again, or a lost conversation did not offer Start new session here: unsaved started={unsavedStarted} offered={unsavedOffered} inLayout={unsavedInLayout} screen='{unsavedScreen}'; lost refused={lostRefused} offered={lostOffered} failure='{lostFailure}' newHere={newHere} screen='{hereScreen}'.");

            // The rail's Resume all button: greyed while every open pane runs, lit once a placeholder appears (here a conversation
            // opened as a tab would do the same), greyed again for the run it starts and after it. The clean run's notice is a
            // brief one: with its hold cut to a second, it is seen closing on its own. Then the launch setting, driven the way the
            // window's load drives it: on (set through the Settings code path and saved), the call starts a new placeholder on
            // its own. Left on for the restore stage, which must still restore placeholders without starting them.
            var railResume = _railItems["resume"].Button;
            var greyedWhenNothing = !railResume.IsEnabled && railResume.Opacity == 1 && ToolTipService.GetToolTip(railResume) is null && ToolTipService.GetToolTip(_railFrames["resume"]) is "Nothing to resume (F5)"
                && ReferenceEquals(_resumeGlyph.Fill, _theme.RailText);
            var buttonHost = PlaceholderTab("PowerShell · rail button", data, "shell");
            var litForPlaceholder = railResume.IsEnabled && railResume.Opacity == 1 && ReferenceEquals(_resumeGlyph.Fill, _theme.RailAccent)
                && ToolTipService.GetToolTip(railResume) is "Resume all (F5)" && ToolTipService.GetToolTip(_railFrames["resume"]) is null;
            _notice.IsOpen = false;
            LastNotice = null;
            BriefNoticeHold = TimeSpan.FromSeconds(1);
            // The panel stays open through the run (it closed for it until November 9, 2026), and its saved state is left as it is.
            if (!_panelOpen) await SetPanelOpen(true, persist: false, animate: false);
            var panelSavedBefore = _preferences.PanelOpen;
            new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(railResume).Invoke();
            var greyedForRun = await EventuallyAsync(() => ResumingAll && !railResume.IsEnabled);
            var panelKeptByResumeAll = _panelOpen && _panel.Width > 0 && _preferences.PanelOpen == panelSavedBefore;
            await WaitForAsync(async () => !ResumingAll && buttonHost.Panes[0].Terminal is { } shown && (await shown.ReadScreenAsync()).Contains("READY"));
            if (!panelKeptByResumeAll) throw new InvalidOperationException($"The rail's Resume all closed the panel, or changed its saved state: open={_panelOpen} width={_panel.Width} saved={_preferences.PanelOpen} before={panelSavedBefore}.");
            await SetPanelOpen(true, persist: false, animate: false);
            var briefNotice = LastNotice is { Title: "Resumed 1 session", Message: "", Brief: true, Severity: InfoBarSeverity.Informational };
            var noticeToasted = briefNotice && await EventuallyAsync(() => !_notice.IsOpen) && _noticeSlide.Y == 0;
            BriefNoticeHold = TimeSpan.FromSeconds(4);
            var resumeAllButton = greyedWhenNothing && litForPlaceholder && greyedForRun && buttonHost.Panes[0].IsRunning && !railResume.IsEnabled
                && briefNotice && ReferenceEquals(_tabs.SelectedItem, shellHost.Item);
            await buttonHost.Panes[0].Terminal!.SendFromRendererAsync("EXIT\r"); await buttonHost.Panes[0].Terminal!.StopAsync();
            await CloseTabAsync(buttonHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!resumeAllButton) throw new InvalidOperationException($"The rail's Resume all button was not greyed with nothing to resume, lit for a placeholder, and greyed for the run it started: nothing={greyedWhenNothing} lit={litForPlaceholder} run={greyedForRun} running={buttonHost.Panes[0].IsRunning} notice='{LastNotice?.Title}' message='{LastNotice?.Message}'.");
            if (!noticeToasted) throw new InvalidOperationException($"The brief notice did not close on its own after its hold: open={_notice.IsOpen} slide={_noticeSlide.Y} notice='{LastNotice?.Title}'.");

            // F5 from inside a live terminal, through the page's chord table: the run the button starts, for a new placeholder.
            var keyHost = PlaceholderTab("PowerShell · F5", data, "shell");
            _notice.IsOpen = false;
            LastNotice = null;
            var f5Taken = await terminal.PressKeyAsync("F5");
            await WaitForAsync(async () => !ResumingAll && keyHost.Panes[0].Terminal is { } started && (await started.ReadScreenAsync()).Contains("READY"));
            // F5 keeps the panel open as the rail button does, and its saved state with it.
            var f5ResumesAll = f5Taken && keyHost.Panes[0].IsRunning && LastNotice is { Title: "Resumed 1 session" } && _panelOpen && _preferences.PanelOpen;
            await keyHost.Panes[0].Terminal!.SendFromRendererAsync("EXIT\r"); await keyHost.Panes[0].Terminal!.StopAsync();
            await CloseTabAsync(keyHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!f5ResumesAll) throw new InvalidOperationException($"F5 inside a live terminal did not resume all, or closed the panel: taken={f5Taken} running={keyHost.Panes[0].IsRunning} notice='{LastNotice?.Title}' panelOpen={_panelOpen} saved={_preferences.PanelOpen}.");

            // Ctrl+F5 from inside a live terminal: this workspace's placeholder starts and another workspace's waits. Then
            // Resume workspace tabs for that other workspace, as its menu asks: it goes there, starts its placeholder, and stays.
            var homeWorkspace = _workspace;
            var otherFolder = PinnedFolders.Normalize(Path.Combine(data, "resume-workspace"));
            Directory.CreateDirectory(otherFolder);
            var otherWorkspace = AddFolder(otherFolder + "\\");
            var hereHost = PlaceholderTab("PowerShell · Ctrl+F5 here", data, "shell");
            var thereHost = PlaceholderTab("PowerShell · Ctrl+F5 there", otherFolder, "shell");
            // CreateTab leaves a tab where it opens; the callers that make one place it by its folder, as here.
            PlaceTab(thereHost, follow: false);
            _tabs.SelectedItem = shellHost.Item;
            _notice.IsOpen = false;
            LastNotice = null;
            var ctrlF5Taken = await terminal.PressKeyAsync("F5", ctrl: true);
            await WaitForAsync(async () => !ResumingAll && hereHost.Panes[0].Terminal is { } started && (await started.ReadScreenAsync()).Contains("READY"));
            var ctrlF5Parts = $"taken={ctrlF5Taken} here={hereHost.Panes[0].IsRunning} there={thereHost.Panes[0].IsRunning} split={ReferenceEquals(WorkspaceOf(hereHost), homeWorkspace)}/{ReferenceEquals(WorkspaceOf(thereHost), otherWorkspace)} stayed={ReferenceEquals(_workspace, homeWorkspace)} notice='{LastNotice?.Title}'";
            var ctrlF5Resumes = ctrlF5Taken && hereHost.Panes[0].IsRunning && !thereHost.Panes[0].IsRunning && thereHost.Panes[0].Terminal is null
                && ReferenceEquals(WorkspaceOf(hereHost), homeWorkspace) && ReferenceEquals(WorkspaceOf(thereHost), otherWorkspace)
                && ReferenceEquals(_workspace, homeWorkspace) && ReferenceEquals(_tabs.SelectedItem, shellHost.Item) && LastNotice is { Title: "Resumed 1 session" }
                && HasResumable(otherWorkspace) && !HasResumable(homeWorkspace!);
            LastNotice = null;
            var thereResumed = await ResumeWorkspaceFromUserAsync(otherWorkspace, new("the verification's Resume workspace tabs", DateTimeOffset.UtcNow));
            await WaitForAsync(async () => thereHost.Panes[0].Terminal is { } started && (await started.ReadScreenAsync()).Contains("READY"));
            ctrlF5Parts += $" menu: resumed={thereResumed} there={thereHost.Panes[0].IsRunning} in={WorkspaceName(_workspace!)} notice='{LastNotice?.Title}'";
            var menuResumes = thereResumed && thereHost.Panes[0].IsRunning && ReferenceEquals(_workspace, otherWorkspace) && LastNotice is { Title: "Resumed 1 session" };
            foreach (var resumed in new[] { hereHost.Panes[0], thereHost.Panes[0] }) { await resumed.Terminal!.SendFromRendererAsync("EXIT\r"); await resumed.Terminal.StopAsync(); }
            await CloseTabAsync(thereHost.Item);
            RemoveFolder(otherFolder);
            if (homeWorkspace is not null && !ReferenceEquals(_workspace, homeWorkspace)) SwitchWorkspace(homeWorkspace);
            await CloseTabAsync(hereHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!ctrlF5Resumes || !menuResumes) throw new InvalidOperationException($"Ctrl+F5 did not resume the current workspace's tabs alone, or Resume workspace tabs did not resume another's and go there: {ctrlF5Parts}.");
            await SetPanelOpen(true, persist: false, animate: false);

            // F1 from inside a live terminal: the keyboard reference in a dialog under the mark and the name, every group and key
            // listed; a second F1 while it is up opens nothing more, and closed it is gone.
            var f1Taken = await terminal.PressKeyAsync("F1");
            var helpOpened = await EventuallyAsync(() => _keyboardHelp is not null && _dialogsOpen == 1);
            var helpText = _keyboardHelp?.Content is DependencyObject helpContent ? TextsIn(helpContent) : [];
            var helpListed = helpText.Contains("Sic") && helpText.Contains("Term") && !helpText.Any(text => text.Contains("Super-Intelligent-Chat", StringComparison.Ordinal))
                && KeyboardReference.All(group => helpText.Contains(group.Group) && group.Shortcuts.All(shortcut => helpText.Contains(shortcut.Keys) && helpText.Contains(shortcut.Action)));
            string? keyboardHelpImage = null;
            if (_keyboardHelp is { } helpDialog)
            {
                await Task.Delay(500);
                // The list lies whole inside the dialog, scrolling where the window is too short for it (November 12, 2026: it ran past the bottom, cut off).
                if (_keyboardHelpScroll is not { } helpScroll || helpScroll.ActualHeight <= 0 || helpScroll.ActualHeight > _root.ActualHeight
                    || Math.Abs(helpScroll.ExtentHeight - helpScroll.ViewportHeight - helpScroll.ScrollableHeight) > 1)
                    throw new InvalidOperationException($"The keyboard reference's list was not held in the dialog: {_keyboardHelpScroll?.ActualHeight} of {_root.ActualHeight}, extent {_keyboardHelpScroll?.ExtentHeight}, viewport {_keyboardHelpScroll?.ViewportHeight}.");
                keyboardHelpImage = Path.Combine(data, "keyboard-help-native.png");
                // Evidence only: a capture refused for an element in a popup does not fail the check.
                try { await CaptureAsync(helpDialog, keyboardHelpImage); }
                catch (Exception exception) { keyboardHelpImage = "not captured: " + exception.Message; }
            }
            await HandleShortcutAsync("help");
            var helpSingle = _dialogsOpen == 1;
            _keyboardHelp?.Hide();
            var helpClosed = await EventuallyAsync(() => _keyboardHelp is null && _dialogsOpen == 0);
            var keyboardHelp = f1Taken && helpOpened && helpListed && helpSingle && helpClosed;
            if (!keyboardHelp) throw new InvalidOperationException($"F1 inside a live terminal did not show the keyboard reference under the mark and the name, once, and close it: taken={f1Taken} opened={helpOpened} listed={helpListed} single={helpSingle} closed={helpClosed}.");

            // The first-run welcome (docs/decisions/first-run.md): never on its own in the smoke, whose data folder is new; asked for,
            // its page loads over the tab area, a skip brings the card up, and closing takes it away. Then the tour steps through its
            // stops: the drop mark flashed over the shell pane at the split stop, the sample meters and context reading painted at the
            // limits stop, and everything put back when it ends.
            var welcomeSkipped = _welcomeFilm is null && _preferences.WelcomeSeen == 0 && !WelcomeCardShown;
            await ShowWelcomeAsync();
            var welcomeFilm = _welcomeFilm;
            var welcomeOpened = welcomeFilm is not null && _welcomeHost.Visibility == Visibility.Visible && _preferences.WelcomeSeen == WelcomeVersion
                && await EventuallyAsync(() => welcomeFilm.Ready.IsCompletedSuccessfully);
            welcomeFilm?.Send(new { type = "skip" });
            var welcomeCard = await EventuallyAsync(() => WelcomeCardShown);
            string? welcomeImage = null;
            if (welcomeFilm is not null && welcomeCard)
            {
                await Task.Delay(700);
                welcomeImage = Path.Combine(data, "welcome-card.png");
                try { await welcomeFilm.CaptureAsync(welcomeImage); }
                catch (Exception exception) { welcomeImage = "not captured: " + exception.Message; }
            }
            CloseWelcome();
            var welcomeClosed = _welcomeFilm is null && _welcomeHost.Visibility == Visibility.Collapsed;
            if (!(welcomeSkipped && welcomeOpened && welcomeCard && welcomeClosed))
                throw new InvalidOperationException($"The welcome did not stay away on its own, open on request, show its card on a skip and close: skipped={welcomeSkipped} opened={welcomeOpened} card={welcomeCard} closed={welcomeClosed}.");
            await StartTourAsync();
            var tourStops = TourRunning && TourStep == 0 && TourStepCount == 8 && _dialogsOpen == 1;
            for (var step = 0; step < 4; step++) await TourNextAsync();
            var tourDropMark = TourStep == 4 && TourStepTitle == "Split, drag, tile" && TourDropMarkShown;
            await TourNextAsync();
            var tourLimits = TourStep == 5 && TourLimitsPreviewed && !TourDropMarkShown && SelectedHost is { } tourHost && FocusedPane(tourHost).Context is { Model: "Opus 4.5", UsedPercent: 72 };
            await Task.Delay(400);
            var tourImage = Path.Combine(data, "tour-native.png");
            try { await CaptureAsync(_root, tourImage); }
            catch (Exception exception) { tourImage = "not captured: " + exception.Message; }
            EndTour();
            var tourEnded = !TourRunning && !TourLimitsPreviewed && !TourDropMarkShown && _dialogsOpen == 0 && (SelectedHost is not { } endedHost || FocusedPane(endedHost).Context is null);
            if (!(tourStops && tourDropMark && tourLimits && tourEnded))
                throw new InvalidOperationException($"The tour did not step through its stops with the drop mark and the sample meters, and end clean: stops={tourStops} dropMark={tourDropMark} limits={tourLimits} ended={tourEnded} step={TourStep} title='{TourStepTitle}'.");
            // The tour left Workspaces showing; the checks below expect Sessions.
            OpenPanel("sessions", persist: false, animate: false);
            SetResumeAllOnLaunch(true);
            var launchHost = PlaceholderTab("PowerShell · resumed on launch", data, "shell");
            _notice.IsOpen = false;
            LastNotice = null;
            ResumeRestoredSessions();
            var launchStarted = ResumingAll && !_notice.IsOpen;
            await WaitForAsync(async () => !ResumingAll && launchHost.Panes[0].Terminal is { } shown && (await shown.ReadScreenAsync()).Contains("READY"));
            var resumeAllOnLaunch = launchStarted && launchHost.Panes[0].IsRunning && LastNotice is { Title: "Resumed 1 session", Brief: true } && ReferenceEquals(_tabs.SelectedItem, shellHost.Item)
                && SavedPreferences(() => new AppPreferences()).ResumeAllOnLaunch;
            // Resumed in a background tab, the pane works while it draws the conversation it brought back, then settles to running:
            // nobody typed into it, so its rest is no turn of the user's and asks for no attention (docs/decisions/agent-presence.md,
            // decision 7). The work is the fixture's, asked for straight through the pseudoconsole, which is not the user's typing.
            await launchHost.Panes[0].Terminal!.WriteAsync("WORK\r");
            await WaitForAsync(() => Task.FromResult(launchHost.Presence == PaneState.Working));
            await WaitForAsync(() => Task.FromResult(launchHost.Presence != PaneState.Working));
            var resumeSettlesQuiet = launchHost.Presence == PaneState.Idle && ReferenceEquals(launchHost.Dot.Fill, _theme.SuccessDot) && TabStateWord(launchHost) == "Running";
            if (!resumeSettlesQuiet) throw new InvalidOperationException($"A session resumed in a background tab did not settle to running: presence={launchHost.Presence} state='{TabStateWord(launchHost)}'.");
            await launchHost.Panes[0].Terminal!.SendFromRendererAsync("EXIT\r"); await launchHost.Panes[0].Terminal!.StopAsync();
            await CloseTabAsync(launchHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            _notice.IsOpen = false;
            if (!resumeAllOnLaunch) throw new InvalidOperationException($"With Resume all sessions on launch on, the restored placeholder was not started without an offer: started={launchStarted} running={launchHost.Panes[0].IsRunning} notice='{LastNotice?.Title}'.");
            // The fixture's placeholder tab comes back, for the checks and the restore stage that expect it.
            await OpenConversationAsync(conversation);
            _tabs.SelectedItem = shellHost.Item;

            // Agent state on the tab, by output. PowerShell sets no title the tracker reads, so a banner makes the pane work and
            // the quiet after it ends the turn. Ended in a background tab, the turn asks for attention until that tab is selected.
            await WaitForAsync(() => Task.FromResult(pane.PresenceState == PaneState.Idle && shellHost.Presence == PaneState.Idle));
            // Output at once answers the line sent and is not work (PresenceTracker.EchoWindow): taken for work, the pane would
            // read as working for the quiet window after it.
            await terminal.SendFromRendererAsync("BANNER\r");
            await Task.Delay(600);
            var echoQuiet = pane.PresenceState == PaneState.Idle;
            if (!echoQuiet) throw new InvalidOperationException("Output answering what the terminal had just sent was taken for work.");
            var bannerSent = System.Diagnostics.Stopwatch.StartNew();
            await terminal.SendFromRendererAsync("WORK\r");
            await WaitForAsync(() => Task.FromResult(pane.PresenceState == PaneState.Working && shellHost.Presence == PaneState.Working));
            var workingAfter = bannerSent.ElapsedMilliseconds;
            var workingShown = SpinnerShown(shellHost) && TabStateWord(shellHost) == "Working…" && pane.StatusText == "Working…" && _statusText.Text.Contains(" · 1 working · ", StringComparison.Ordinal);
            if (!workingShown) _options.Trace($"presence-working spinner={SpinnerShown(shellHost)} tab='{TabStateWord(shellHost)}' pane='{pane.StatusText}' status='{_statusText.Text}'");
            await WaitForAsync(() => Task.FromResult(pane.PresenceState == PaneState.Idle));
            var quietFor = bannerSent.ElapsedMilliseconds - workingAfter;
            var idleShown = shellHost.Presence == PaneState.Idle && shellHost.Ring.Visibility == Visibility.Collapsed && TabStateWord(shellHost) == "Running" && pane.StatusText == "Running"
                && !_statusText.Text.Contains("working", StringComparison.Ordinal);
            var presencePane = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · presence", data, "");
            var presenceHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), presencePane.Title, [presencePane]));
            await terminal.SendFromRendererAsync("WORK\r");
            await WaitForAsync(() => Task.FromResult(shellHost.Presence == PaneState.Attention));
            var attentionShown = shellHost.Dot.Visibility == Visibility.Visible && ReferenceEquals(shellHost.Dot.Fill, _theme.WarningDot) && shellHost.Title.FontWeight.Weight == 600
                && TabStateWord(shellHost) == "Your turn";
            // The Your turn dot blinks at the cursor's cadence, dimming rather than vanishing, whatever Menu Animation says.
            var motionBefore = _preferences.Motion;
            SetMotion(MotionSetting.Off);
            // Opacity comes back as a float, so the dim is read with a tolerance.
            var blinkDims = Blinking && await EventuallyAsync(() => Math.Abs(shellHost.Dot.Opacity - StateDots.BlinkDim) < 0.01);
            SetMotion(motionBefore);
            if (!blinkDims) throw new InvalidOperationException($"The Your turn dot did not blink with Menu Animation off: blinking={Blinking} opacity={shellHost.Dot.Opacity}.");
            _tabs.SelectedItem = shellHost.Item;
            await WaitForAsync(() => Task.FromResult(shellHost.Presence == PaneState.Idle));
            var attentionCleared = shellHost.Title.FontWeight.Weight == 400 && TabStateWord(shellHost) == "Running";
            var presenceHeuristic = workingAfter < 1500 && workingShown && quietFor >= PresenceTracker.QuietAfter.TotalMilliseconds - 800
                && quietFor < PresenceTracker.QuietAfter.TotalMilliseconds + 2000 && idleShown && attentionShown && attentionCleared;
            if (!presenceHeuristic)
                throw new InvalidOperationException($"Output did not mark the shell pane working and its quiet idle, or a turn that ended in a background tab did not ask for attention and clear on selection: working after {workingAfter} ms shown={workingShown}, quiet for {quietFor} ms shown={idleShown}, attention={attentionShown} cleared={attentionCleared}.");

            // By title. A pane whose agent is Claude Code runs the fixture, which sets the titles Claude Code sets: the working
            // glyph makes it work, and the idle glyph ends the turn even though output continues, as Claude Code's blinking dot
            // does at a permission prompt.
            var claudePane = new SavedPane(Guid.NewGuid().ToString("N"), "claude", "Claude Code · presence fixture", data, "");
            var claudeHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), claudePane.Title, [claudePane]));
            var agentPane = claudeHost.Panes[0];
            await agentPane.StartAsync(CreateShellRequest(SmokeShellArguments, data), SmokeStart);
            var agentTerminal = agentPane.Terminal!;
            await WaitForAsync(async () => (await agentTerminal.ReadScreenAsync()).Contains("READY"));
            await WaitForAsync(() => Task.FromResult(agentPane.PresenceState == PaneState.Idle));
            await agentTerminal.SendFromRendererAsync("TITLE:\\u25D0 Fixture turn\r");
            await WaitForAsync(() => Task.FromResult(agentPane.PresenceState == PaneState.Working && claudeHost.Presence == PaneState.Working));
            await Task.Delay(PresenceTracker.QuietAfter + TimeSpan.FromMilliseconds(600));
            var titleHoldsWorking = agentPane.PresenceState == PaneState.Working && SpinnerShown(claudeHost) && TabStateWord(claudeHost) == "Working…";
            _tabs.SelectedItem = presenceHost.Item;
            await agentTerminal.SendFromRendererAsync("TITLE:\\u2733 Fixture turn\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Attention));
            await agentTerminal.SendFromRendererAsync("WORK\r");
            await WaitForAsync(async () => (await agentTerminal.ReadScreenAsync()).Contains("████████"));
            await Task.Delay(600);
            var idleTitleWins = agentPane.PresenceState == PaneState.Attention && TabStateWord(claudeHost) == "Your turn";
            _tabs.SelectedItem = claudeHost.Item;
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Idle));
            var presenceTitle = titleHoldsWorking && idleTitleWins && agentPane.StatusText == "Running";

            // By report. The agent's own status (OSC 7501, docs/decisions/program-status.md) outranks its titles: working holds
            // under an idle title. Blocked is Needs you, in the danger hue, blinking out of view and steady in view, where the pane
            // header says it too. Done is an unseen result in Your turn's hue, seen at once in view and otherwise until a look;
            // error is Failed in the danger hue, cleared by a look as well. Clear hands the pane back to its titles.
            _tabs.SelectedItem = presenceHost.Item;
            await agentTerminal.SendFromRendererAsync("STATUS:state=working:app=fixture\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Working));
            await agentTerminal.SendFromRendererAsync("TITLE:\\u2733 Fixture turn\r");
            await Task.Delay(PresenceTracker.QuietAfter + TimeSpan.FromMilliseconds(600));
            var reportHoldsWorking = claudeHost.Presence == PaneState.Working && SpinnerShown(claudeHost) && agentPane.Presence.Reporting;
            await agentTerminal.SendFromRendererAsync("STATUS:state=blocked:kind=permission\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Blocked));
            var blockedShown = ReferenceEquals(claudeHost.Dot.Fill, _theme.DangerDot) && TabStateWord(claudeHost) == "Needs you (permission)" && Blinking
                && claudeHost.Title.FontWeight.Weight == 600 && _statusText.Text.Contains(" · 1 needs you · ", StringComparison.Ordinal);
            _tabs.SelectedItem = claudeHost.Item;
            await WaitForAsync(() => Task.FromResult(agentPane.StatusText == "Needs you"));
            var blockedInView = claudeHost.Presence == PaneState.Blocked && !Blinking && claudeHost.Dot.Opacity == 1 && ReferenceEquals(claudeHost.Dot.Fill, _theme.DangerDot);
            await agentTerminal.SendFromRendererAsync("STATUS:state=working\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Working && agentPane.StatusText == "Working…"));
            await agentTerminal.SendFromRendererAsync("STATUS:state=done\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence != PaneState.Working));
            var doneInViewSeen = claudeHost.Presence == PaneState.Idle && agentPane.StatusText == "Running";
            _tabs.SelectedItem = presenceHost.Item;
            await agentTerminal.SendFromRendererAsync("STATUS:state=working\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Working));
            await agentTerminal.SendFromRendererAsync("STATUS:state=done\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Done));
            var doneShown = ReferenceEquals(claudeHost.Dot.Fill, _theme.WarningDot) && TabStateWord(claudeHost) == "Done" && Blinking && claudeHost.Title.FontWeight.Weight == 600;
            _tabs.SelectedItem = claudeHost.Item;
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Idle));
            _tabs.SelectedItem = presenceHost.Item;
            await agentTerminal.SendFromRendererAsync("STATUS:state=error\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Error));
            var errorShown = ReferenceEquals(claudeHost.Dot.Fill, _theme.DangerDot) && TabStateWord(claudeHost) == "Failed" && Blinking;
            _tabs.SelectedItem = claudeHost.Item;
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Idle));
            await agentTerminal.SendFromRendererAsync("STATUS:state=clear\r");
            await WaitForAsync(() => Task.FromResult(!agentPane.Presence.Reporting));
            var presenceReport = reportHoldsWorking && blockedShown && blockedInView && doneInViewSeen && doneShown && errorShown && claudeHost.Presence == PaneState.Idle;
            if (!presenceReport)
                throw new InvalidOperationException($"The agent's status reports did not decide the pane's state: working held under an idle title={reportHoldsWorking}, blocked shown={blockedShown} and steady in view={blockedInView}, done seen in view={doneInViewSeen} and shown out of it={doneShown}, error shown={errorShown}, cleared={claudeHost.Presence}.");

            // The ready chime, turned on through the Settings code path. With the window counted as inactive, a turn typed into the
            // selected Claude Code pane that works on past the minimum after the keystroke rings it once and asks for attention,
            // which the window coming back into view clears; a quicker one does not ring. With the window in view the same turn is
            // being watched: no chime, no attention. One that ends while the window is in the tray rings, which is what it is for.
            // The smoke counts chimes instead of sounding them; that the audio device takes the chime's format is checked by
            // playing its length of silence. Left on for the restore stage.
            SetReadyChime(true);
            var chimesBefore = ChimesRung;
            async Task TurnAsync(int milliseconds)
            {
                await agentTerminal.SendFromRendererAsync($"TURN:{milliseconds}\r");
                await WaitForAsync(() => Task.FromResult(agentPane.PresenceState == PaneState.Working));
                await WaitForAsync(() => Task.FromResult(agentPane.PresenceState != PaneState.Working));
            }
            // A turn the agent reports the end of (OSC 7501) ends when it says so, at once and however short, and a result that
            // arrives out of view rings and shows as Done until the window is looked at.
            SetSmokeWindowActive(false);
            await agentTerminal.SendFromRendererAsync("STATUS:state=working\r");
            await WaitForAsync(() => Task.FromResult(agentPane.PresenceState == PaneState.Working));
            await agentTerminal.SendFromRendererAsync("STATUS:state=done\r");
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Done));
            var reportedTurnChimed = ChimesRung == chimesBefore + 1;
            SetSmokeWindowActive(true);
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Idle));
            await agentTerminal.SendFromRendererAsync("STATUS:state=clear\r");
            await WaitForAsync(() => Task.FromResult(!agentPane.Presence.Reporting));
            if (!reportedTurnChimed) throw new InvalidOperationException($"A turn the agent reported done out of view did not ring the chime once: rung={ChimesRung - chimesBefore}.");
            chimesBefore = ChimesRung;
            SetSmokeWindowActive(false);
            await TurnAsync(1000);
            var quickTurnQuiet = ChimesRung == chimesBefore;
            // Attention outranks a new turn, so the quick turn's is cleared by a look at the window before the next.
            SetSmokeWindowActive(true);
            SetSmokeWindowActive(false);
            var fullTurn = (int)PresenceTracker.MinimumTurn.TotalMilliseconds + 600;
            await TurnAsync(fullTurn);
            await WaitForAsync(() => Task.FromResult(claudeHost.Presence == PaneState.Attention));
            var turnChimed = ChimesRung == chimesBefore + 1;
            SetSmokeWindowActive(true);
            var inViewClears = claudeHost.Presence == PaneState.Idle;
            await TurnAsync(fullTurn);
            await Task.Delay(600);
            var watchedQuiet = ChimesRung == chimesBefore + 1 && claudeHost.Presence == PaneState.Idle;
            if (!inViewClears || !watchedQuiet) throw new InvalidOperationException($"A turn in a window in view was not treated as watched: attention cleared={inViewClears} watched turn quiet={watchedQuiet} presence={claudeHost.Presence} rung={ChimesRung - chimesBefore}.");
            SaveLayout(); FlushState(); AppWindow.Hide();
            await TurnAsync(fullTurn);
            var trayChimed = ChimesRung == chimesBefore + 2 && !AppWindow.IsVisible;
            ShowFromTray();
            await WaitForAsync(() => Task.FromResult(AppWindow.IsVisible));
            SetReadyChime(false);
            RingReadyChime();
            var offQuiet = ChimesRung == chimesBefore + 2;
            SetReadyChime(true);
            var chimePlayable = await Task.Run(ChimeSound.PlaySilence);
            var readyChime = quickTurnQuiet && turnChimed && trayChimed && offQuiet && chimePlayable && SavedPreferences(() => new AppPreferences()).ReadyChime;
            if (!readyChime) throw new InvalidOperationException($"The ready chime did not ring for a turn the user started, and only then: quick turn quiet={quickTurnQuiet} full turn={turnChimed} from the tray={trayChimed} off quiet={offQuiet} playable={chimePlayable} rung={ChimesRung - chimesBefore}.");
            await agentTerminal.SendFromRendererAsync("EXIT\r");
            await agentTerminal.StopAsync();
            await CloseTabAsync(claudeHost.Item);
            await CloseTabAsync(presenceHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            if (!presenceTitle) throw new InvalidOperationException($"The Claude Code titles did not decide the pane's state: working held by title={titleHoldsWorking}, idle title over output={idleTitleWins}.");

            var beforeCollapse = await terminal.GetDimensionsAsync();
            // Settings → Menu Animation, On: the slide runs whatever Windows says about animation effects, and to its end in its own
            // time, neither skipped nor left to the fallback timeout.
            SetMotion(MotionSetting.On);
            var slide = System.Diagnostics.Stopwatch.StartNew();
            await SetPanelOpen(false);
            var panelSlid = PanelSlides && slide.ElapsedMilliseconds is >= 60 and < 900;
            await WaitForAsync(async () => (await terminal.GetDimensionsAsync()).Columns > beforeCollapse.Columns);
            var collapsedPathFits = pane.PathFitsBest();
            var compactShellImage = await CaptureShellAsync(Path.Combine(data, "compact-native.png"), terminal);
            var panelCollapsed = !_panelOpen && _panel.Width == 0 && _panel.Visibility == Visibility.Collapsed && pane.IsRunning && !_preferences.PanelOpen;
            await terminal.SendFromRendererAsync("PING:FOCUSED\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:FOCUSED"));
            var focusedShellImage = await CaptureShellAsync(Path.Combine(data, "focused-native.png"), terminal);
            var focusedTerminalImage = Path.Combine(data, "focused-terminal.png");
            await terminal.CaptureAsync(focusedTerminalImage);
            // Off: the panel is back at once. Then On again, the default, which is what the settings file keeps.
            SetMotion(MotionSetting.Off);
            var snap = System.Diagnostics.Stopwatch.StartNew();
            await SetPanelOpen(true);
            var panelSnapped = !PanelSlides && snap.ElapsedMilliseconds < 60 && _panel.Width == PanelLayout.DefaultWidth;
            SetMotion(MotionSetting.On);
            var motionSetting = panelSlid && panelSnapped && PanelSlides && SavedPreferences(() => new AppPreferences()).Motion == MotionSetting.On;
            await WaitForAsync(async () => (await terminal.GetDimensionsAsync()).Columns <= beforeCollapse.Columns);
            await WaitForAsync(() => Task.FromResult(pane.PathFitsBest()));
            var panelExpanded = _panelOpen && _panel.Width == PanelLayout.DefaultWidth && _panel.Visibility == Visibility.Visible && pane.IsRunning && _preferences.PanelOpen;
            OpenPanel("workspaces");
            var workspacesPanel = ReferenceEquals(_panelContent.Content, _panels["workspaces"].Content) && _preferences.ActivePanel == "workspaces";
            // The Folders panel (docs/decisions/workspaces.md): the data folder, the one listed folder, has a row with its tree open under
            // it, listing its subfolders (the fixtures folder among them); folded by its row, saved by key, and opened again; the rows fill
            // their containers, so the Explorer button sits at the row's edge. The tree is left open for the stages after this.
            var pinnedFolder = PinnedFolders.Normalize(data);
            WorkspaceRowView HomeView() => _workspaceRows[_workspaces[0].Id];
            OpenPanel("folders");
            var foldersPanel = ReferenceEquals(_panelContent.Content, _panels["folders"].Content) && _preferences.ActivePanel == "folders";
            TreeView HomeTree() => FolderTreeOf(data) ?? throw new InvalidOperationException("The data folder has no tree in the Folders panel.");
            var foldersOpen = await EventuallyAsync(() => FolderRowOf(data) is { ActualWidth: > 0 } && HomeTree().Visibility == Visibility.Visible && !FolderFolded(data)
                && HomeTree().RootNodes.Any(node => _folderPaths.GetValueOrDefault(node) == Path.Combine(data, "fixtures")));
            ToggleFolderFold(data);
            var foldersFolded = FolderFolded(data) && HomeTree().Visibility == Visibility.Collapsed && SavedPreferences(() => new AppPreferences()).FoldedSections.Contains(SectionFolds.Root(data));
            ToggleFolderFold(data);
            var foldersOpened = await EventuallyAsync(() => !FolderFolded(data) && HomeTree().Visibility == Visibility.Visible && HomeTree().RootNodes.Count > 0);
            var folderRowStretched = await EventuallyAsync(() => HomeTree().RootNodes.Count > 0 && FolderRowView(HomeTree().RootNodes[0]) is Grid { ActualWidth: > 200 });
            var foldersImage = await CaptureShellAsync(Path.Combine(data, "folders-native.png"), terminal);
            var workspaceFolders = workspacesPanel && foldersPanel && foldersOpen && foldersFolded && foldersOpened && folderRowStretched;
            if (!workspaceFolders) throw new InvalidOperationException($"The Folders panel did not list the data folder's tree, fold it and open it again: workspaces={workspacesPanel} folders={foldersPanel} open={foldersOpen} folded={foldersFolded} opened={foldersOpened} stretched={folderRowStretched} roots=[{string.Join("; ", FolderTreeOf(data)?.RootNodes.Select(node => _folderPaths.GetValueOrDefault(node)) ?? [])}].");
            // Folders from the history, through the first run's code path with a history of its own: a folder with three conversations
            // joins the list after the folders there are, with a row and a workspace; a subfolder of the data folder, under a listed
            // folder, and a folder with two conversations do not. Removed, it stays out when the history is read again, which leaves the list as it was.
            var commonFolder = PinnedFolders.Normalize(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sic-smoke-common-" + Guid.NewGuid().ToString("N"))).FullName);
            var suggestedWorkspaces = false;
            try
            {
                var now = DateTimeOffset.Now;
                var activity = Enumerable.Range(0, 3).Select(index => ((string?)commonFolder, now.AddHours(-index)))
                    .Concat(Enumerable.Range(0, 4).Select(index => ((string?)Path.Combine(data, "inside"), now.AddHours(-index))))
                    .Concat(Enumerable.Range(0, 2).Select(index => ((string?)Path.GetTempPath(), now.AddHours(-index)))).ToList();
                var foldersBefore = Folders.Count;
                var firstRun = await AddFoldersFromHistoryAsync(activity, quiet: true);
                var commonMade = firstRun.SequenceEqual([commonFolder]) && Folders.SequenceEqual([pinnedFolder, commonFolder]) && _workspaces.Count == 2 && _workspaces[^1].Home == commonFolder && WorkspaceName(_workspaces[^1]) == Paths.Leaf(commonFolder)
                    && SavedPreferences(() => new AppPreferences()).AutoPinnedFolders.SequenceEqual([commonFolder]) && ReferenceEquals(_workspace, _workspaces[0]) && FolderRowOf(commonFolder) is not null;
                // The list's order (MainWindow.FolderDrags.cs), which the Folders list, the workspaces and the indicator's list share,
                // each read back: a move; a drag's landing place in the Folders panel from its rows as laid out, and the drop; the
                // three sorts, each from the order opposite to the one it should give; and back to the order the later stages expect.
                // The indicator's list is built as it opens, and a menu shown again straight after it was hidden keeps its old items until it has closed, so that part is waited for.
                async Task<bool> OrderIs(params string[] expected) => Folders.SequenceEqual(expected) && SavedPreferences(() => new AppPreferences()).Folders?.SequenceEqual(expected) == true
                    && _workspaces.Select(workspace => workspace.Home).SequenceEqual(expected)
                    && await EventuallyAsync(() => WorkspaceSwitcherItems().OfType<MenuFlyoutItem>().Select(item => item.Text).SequenceEqual(expected.Select(Paths.Leaf)));
                MoveFolderTo(commonFolder, 0);
                var orderMoved = await OrderIs(commonFolder, pinnedFolder);
                var dropPlaces = await EventuallyAsync(() => FolderDropIndexForCheck(false, pinnedFolder, -1000) == 0 && FolderDropIndexForCheck(false, pinnedFolder, 100_000) is null
                    && FolderDropIndexForCheck(false, commonFolder, 100_000) == 2 && FolderDropIndexForCheck(false, commonFolder, -1000) is null);
                DropFolderAt(commonFolder, 2);
                var orderDropped = await OrderIs(pinnedFolder, commonFolder);
                var byName = new[] { pinnedFolder, commonFolder }.OrderBy(Paths.Leaf, StringComparer.CurrentCultureIgnoreCase).ToArray();
                MoveFolderTo(byName[1], 0);
                SortFolders(FolderSort.Name);
                var sortedByName = await OrderIs(byName);
                MoveFolderTo(commonFolder, 0);
                SortFolders(FolderSort.OpenSessions);
                var sortedByOpen = HostsOf(_workspaces[0]).Any() && await OrderIs(pinnedFolder, commonFolder);
                await SortFoldersByRecentAsync([(Path.Combine(data, "inside"), now.AddHours(-2)), (commonFolder, now.AddHours(-1)), (Path.GetTempPath(), now)]);
                var sortedByRecent = await OrderIs(commonFolder, pinnedFolder);
                MoveFolderTo(pinnedFolder, 0);
                var folderOrder = orderMoved && dropPlaces && orderDropped && sortedByName && sortedByOpen && sortedByRecent && await OrderIs(pinnedFolder, commonFolder);
                if (!folderOrder) throw new InvalidOperationException($"The Folders list did not take the order asked for: moved={orderMoved} dropPlaces={dropPlaces} dropped={orderDropped} name={sortedByName} open={sortedByOpen} recent={sortedByRecent} folders=[{string.Join("; ", Folders)}].");
                RemoveFolder(commonFolder);
                var secondRun = await AddFoldersFromHistoryAsync(activity, quiet: true);
                suggestedWorkspaces = commonMade && secondRun.Count == 0 && Folders.Count == foldersBefore && Folders.SequenceEqual([pinnedFolder]) && _workspaces.Count == 1;
                if (!suggestedWorkspaces) throw new InvalidOperationException($"Folders from history did not add the expected folder once: made={commonMade} first=[{string.Join("; ", firstRun)}] second=[{string.Join("; ", secondRun)}] folders=[{string.Join("; ", Folders)}].");
            }
            finally { Directory.Delete(commonFolder); }
            // Usage limits (docs/decisions/usage-limits.md). First SicTermStatusLine.exe, Claude Code's status line, started as Claude
            // Code starts it, with the session's JSON on standard input: it keeps the reading in the data folder and exits. Then the
            // status bar's groups, drawn from that reading and a ChatGPT one: Claude's 5-hour window on pace and its week near the
            // limit, ChatGPT's (used by Codex and Hermes, both marks shown) 5-hour window ahead of pace and its week on pace, each
            // meter in its state's colour, in that order.
            var limitsNow = DateTimeOffset.Now;
            // The session's context window rides along (docs/decisions/context-window.md): a session of its own, filling, under a named model.
            var contextSession = Guid.NewGuid().ToString();
            var statusInput = JsonSerializer.Serialize(new
            {
                session_id = contextSession, workplace = new { current_dir = data },
                model = new { id = "claude-opus-4-5", display_name = "Opus 4.5" },
                rate_limits = new
                {
                    five_hour = new { used_percentage = 31, resets_at = (limitsNow + Sic.Core.Limits.UsageLimits.FiveHours * 0.52).ToUnixTimeSeconds() },
                    seven_day = new { used_percentage = 95, resets_at = limitsNow.AddDays(3).ToUnixTimeSeconds() }
                },
                context_window = new
                {
                    context_window_size = 200000, used_percentage = 73, remaining_percentage = 27,
                    current_usage = new { input_tokens = 100000, output_tokens = 1200, cache_creation_input_tokens = 20000, cache_read_input_tokens = 26000 }
                }
            });
            var statusLineClock = System.Diagnostics.Stopwatch.StartNew();
            var statusLineStart = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, Sic.Core.Limits.ClaudeStatusLine.ExecutableName))
            {
                ArgumentList = { "--data-dir", data },
                RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            string statusLinePrinted;
            using (var statusLine = System.Diagnostics.Process.Start(statusLineStart)!)
            {
                await statusLine.StandardInput.WriteAsync(statusInput);
                statusLine.StandardInput.Close();
                statusLinePrinted = await statusLine.StandardOutput.ReadToEndAsync();
                await statusLine.WaitForExitAsync();
            }
            var statusLineMs = statusLineClock.ElapsedMilliseconds;
            var claudeKept = Sic.Core.Limits.ClaudeStatusLine.Read(data);
            var statusLineKept = claudeKept is { Short.UsedPercent: 31, Long.UsedPercent: 95 };
            if (!statusLineKept) throw new InvalidOperationException($"SicTermStatusLine.exe did not keep the reading: kept={(claudeKept is null ? "none" : Sic.Core.Limits.UsageLimits.Serialize(claudeKept))} printed='{statusLinePrinted}' ms={statusLineMs}.");
            _limitReadings["claude"] = claudeKept!;
            _limitReadings["chatgpt"] = new Sic.Core.Limits.LimitReading(
                new Sic.Core.Limits.LimitWindow(58, limitsNow + Sic.Core.Limits.UsageLimits.FiveHours * 0.65, Sic.Core.Limits.UsageLimits.FiveHours),
                new Sic.Core.Limits.LimitWindow(8, limitsNow + Sic.Core.Limits.UsageLimits.SevenDays * 0.7, Sic.Core.Limits.UsageLimits.SevenDays), limitsNow);
            RenderLimits([("claude", ["claude"]), ("chatgpt", ["codex", "hermes"])]);
            var limitGroups = _limitsPanel.Children.OfType<StackPanel>().ToArray();
            Microsoft.UI.Xaml.Media.Brush? MeterFill(StackPanel group, int window) =>
                group.Children.OfType<StackPanel>().Skip(1).ElementAtOrDefault(window)?.Children.OfType<Grid>().FirstOrDefault()?.Children.OfType<Border>().Skip(1).FirstOrDefault()?.Background;
            var usageLimits = limitGroups.Length == 2 && _limitsPanel.Children.Count == 4
                && limitGroups.Select(group => group.Tag as string).SequenceEqual(["claude", "chatgpt"])
                && limitGroups.Select(group => (group.Children[0] as StackPanel)?.Children.Count).SequenceEqual([1, 2])
                && ReferenceEquals(MeterFill(limitGroups[0], 0), _theme.LimitOnPace) && ReferenceEquals(MeterFill(limitGroups[0], 1), _theme.LimitNear)
                && ReferenceEquals(MeterFill(limitGroups[1], 0), _theme.LimitAhead) && ReferenceEquals(MeterFill(limitGroups[1], 1), _theme.LimitOnPace)
                && ToolTipService.GetToolTip(limitGroups[0]) is string claudeTip && claudeTip.StartsWith("5h: Resets ", StringComparison.Ordinal) && claudeTip.Contains(Environment.NewLine + "7d: Resets ", StringComparison.Ordinal) && !claudeTip.Contains("%", StringComparison.Ordinal)
                && ToolTipService.GetToolTip(limitGroups[1]) is string codexTip && codexTip.StartsWith("5h: Resets ", StringComparison.Ordinal) && codexTip.Contains(" @ ", StringComparison.Ordinal) && !codexTip.Contains("@ @", StringComparison.Ordinal);
            var limitsImage = await CaptureShellAsync(Path.Combine(data, "usage-limits.png"), terminal);
            if (!usageLimits) throw new InvalidOperationException($"The usage limits were not drawn as expected: groups={limitGroups.Length} children={_limitsPanel.Children.Count} tips=[{string.Join(" | ", limitGroups.Select(group => ToolTipService.GetToolTip(group)))}].");
            // A reading kept from before this run is drawn grey, its number dimmed and never bold, until the plan is read again.
            _limitReadings["claude"] = claudeKept! with { ReadAt = _limitsSince.AddHours(-1) };
            RenderLimits([("claude", ["claude"])]);
            var earlierGroup = _limitsPanel.Children.OfType<StackPanel>().FirstOrDefault();
            var limitsEarlier = earlierGroup is not null && ReferenceEquals(MeterFill(earlierGroup, 0), _theme.LimitUnknown) && ReferenceEquals(MeterFill(earlierGroup, 1), _theme.LimitUnknown)
                && ToolTipService.GetToolTip(earlierGroup) is string earlierTip && earlierTip.StartsWith("Last reading ", StringComparison.Ordinal) && earlierTip.Contains(Environment.NewLine + "5h: Resets ", StringComparison.Ordinal);
            if (!limitsEarlier) throw new InvalidOperationException($"A reading from before this run was not drawn grey: tip='{(earlierGroup is null ? "none" : ToolTipService.GetToolTip(earlierGroup))}'.");
            _limitReadings.Clear();
            RenderLimits();
            // The context window (docs/decisions/context-window.md): the helper kept the session's reading in its own file, and a
            // running Claude Code pane of that session (a tab of its own running the fixture, as the presence check makes one)
            // shows it in its header through the window's own read: the meter at 73 % in the header's amber, "73%" beside it,
            // the tokens and the model in the words. The PowerShell pane shows none, the meter goes when the setting is turned off
            // and comes back with it, and a pane that stops loses it.
            var contextKept = Sic.Core.Limits.ClaudeStatusLine.ReadContext(data, contextSession) is { UsedPercent: 73, WindowTokens: 200000, UsedTokens: 146000, Model: "Opus 4.5" };
            var contextHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "Claude Code · context fixture", [new SavedPane(Guid.NewGuid().ToString("N"), "claude", "Claude Code · context fixture", data, "", contextSession)]));
            var contextPane = contextHost.Panes[0];
            await contextPane.StartAsync(CreateShellRequest(SmokeShellArguments, data), SmokeStart);
            var contextTerminal = contextPane.Terminal!;
            await WaitForAsync(async () => (await contextTerminal.ReadScreenAsync()).Contains("READY"));
            await RefreshContextAsync(contextSession);
            var contextMeter = contextPane.ContextShown && contextPane.ContextMeterShown && contextPane.ContextText == "73%" && contextPane.ContextModelText == "Opus 4.5"
                && ReferenceEquals(contextPane.ContextFill, contextHost.Paint.HeaderWarning)
                && contextPane.ContextWords == "Context window · 146k of 200k tokens · Opus 4.5" && !pane.ContextShown;
            SetShowUsageLimits(false);
            await RefreshContextAsync(contextSession);
            var contextHidden = !contextPane.ContextShown;
            SetShowUsageLimits(true);
            await RefreshContextAsync(contextSession);
            var contextBack = contextPane.ContextShown;
            await contextTerminal.SendFromRendererAsync("EXIT\r");
            await contextTerminal.StopAsync();
            await RefreshContextAsync(contextSession);
            var contextGoneWithThePane = !contextPane.ContextShown;
            // Unlinked before it closes, so the close starts no history refresh under the checks that follow.
            contextPane.UpdateIdentity(contextPane.Descriptor with { SessionId = null });
            await CloseTabAsync(contextHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            // Codex's reading comes from its rollout, the one the catalog records for the session: a fixture rollout with a turn
            // context naming the model and a token count of 120,000 against a usable window of 475,000, which Codex's own rule
            // (12,000 off both) shows as 23 % used, read through the window's own path into a Codex fixture pane's header.
            var codexSession = Guid.NewGuid().ToString();
            var codexRollout = Path.Combine(Directory.CreateDirectory(Path.Combine(data, "context-fixture")).FullName, "rollout-2026-10-16T09-38-57-" + codexSession + ".jsonl");
            File.WriteAllLines(codexRollout, [
                JsonSerializer.Serialize(new { timestamp = "2026-10-16T14:38:59Z", type = "session_meta", payload = new { id = codexSession, cwd = data } }),
                JsonSerializer.Serialize(new { timestamp = "2026-10-16T14:39:00Z", type = "turn_context", payload = new { cwd = data, approval_policy = "on-request", model = "gpt-6.1-sol", summary = "none" } }),
                JsonSerializer.Serialize(new { timestamp = "2026-10-16T14:39:02Z", type = "event_msg", payload = new { type = "token_count", info = new { total_token_usage = new { total_tokens = 120000 }, last_token_usage = new { input_tokens = 119986, cached_input_tokens = 13184, output_tokens = 14, reasoning_output_tokens = 0, total_tokens = 120000 }, model_context_window = 475000 }, rate_limits = (object?)null } })]);
            _catalog.Upsert(new Conversation("codex", codexSession, "Codex context fixture", data, limitsNow, limitsNow, codexRollout, Path.Combine(data, "fixtures", "codex"), ""));
            var codexHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "Codex · context fixture", [new SavedPane(Guid.NewGuid().ToString("N"), "codex", "Codex · context fixture", data, Path.Combine(data, "fixtures", "codex"), codexSession)]));
            var codexPane = codexHost.Panes[0];
            await codexPane.StartAsync(CreateShellRequest(SmokeShellArguments, data), SmokeStart);
            var codexTerminal = codexPane.Terminal!;
            await WaitForAsync(async () => (await codexTerminal.ReadScreenAsync()).Contains("READY"));
            await RefreshContextAsync(codexSession);
            var codexMeter = codexPane.ContextShown && codexPane.ContextMeterShown && codexPane.ContextText == "23%" && codexPane.ContextModelText == "gpt-6.1-sol"
                && ReferenceEquals(codexPane.ContextFill, codexHost.Paint.HeaderSuccess) && codexPane.ContextWords == "Context window · 120k of 475k tokens · gpt-6.1-sol";
            // The day's rollout folder is watched: a plan reading Codex appends reaches the bar within a second or two of the
            // write, with the timer stopped so only the watcher can have brought it (the folder is made here, after the watcher
            // first looked, and pointed at as a timer tick would point it).
            var codexDayFolder = Directory.CreateDirectory(Sic.Core.Limits.UsageLimits.CodexDayFolder(Path.Combine(data, "fixtures", "codex"), DateTime.Now)).FullName;
            _limitsTimer?.Stop();
            WatchCodexRollouts();
            var codexWatched = _codexWatcher is not null && string.Equals(_codexWatchedFolder, codexDayFolder, StringComparison.OrdinalIgnoreCase);
            var watchedSession = Guid.NewGuid().ToString();
            var watchedAt = DateTimeOffset.UtcNow;
            File.WriteAllLines(Path.Combine(codexDayFolder, "rollout-2026-10-16T09-40-00-" + watchedSession + ".jsonl"), [
                JsonSerializer.Serialize(new { timestamp = watchedAt.ToString("O"), type = "session_meta", payload = new { id = watchedSession, cwd = data } }),
                JsonSerializer.Serialize(new { timestamp = watchedAt.ToString("O"), type = "event_msg", payload = new { type = "token_count", info = (object?)null,
                    rate_limits = new { limit_id = "codex", primary = new { used_percent = 33.0, window_minutes = 300, resets_at = watchedAt.AddHours(2).ToUnixTimeSeconds() }, secondary = (object?)null } } })]);
            var codexLimitsWatched = await EventuallyAsync(() => _limitReadings.GetValueOrDefault("chatgpt") is { Short.UsedPercent: 33 });
            _limitsTimer?.Start();
            await codexTerminal.SendFromRendererAsync("EXIT\r");
            await codexTerminal.StopAsync();
            codexPane.UpdateIdentity(codexPane.Descriptor with { SessionId = null });
            await CloseTabAsync(codexHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            var contextWindow = contextKept && contextMeter && contextHidden && contextBack && contextGoneWithThePane && codexMeter && codexWatched && codexLimitsWatched;
            if (!contextWindow) throw new InvalidOperationException($"The context window was not kept or shown as expected: kept={contextKept} meter={contextMeter} (shown={contextPane.ContextShown} text='{contextPane.ContextText}' words='{contextPane.ContextWords}') hidden={contextHidden} back={contextBack} gone={contextGoneWithThePane} codex={codexMeter} (shown={codexPane.ContextShown} text='{codexPane.ContextText}' model='{codexPane.ContextModelText}' words='{codexPane.ContextWords}') watched={codexWatched} limitsWatched={codexLimitsWatched} (chatgpt={(_limitReadings.GetValueOrDefault("chatgpt") is { } chatGpt ? Sic.Core.Limits.UsageLimits.Serialize(chatGpt) : "none")}).");
            // An emoji for the data folder (docs/decisions/folder-emoji.md), set through the menu's code path: it shows on the workspace's
            // row and the folder's row in place of the folder glyph, and on the indicator while that workspace shows, and not on the tab or
            // the pane of the live terminal working in the folder (they lost theirs on November 8, 2026); it is saved; two characters are
            // refused, which clears it; it is left set for the restore stage.
            SetFolderEmoji(data, "🍁");
            var terminalHost = HostOf(pane)!;
            var chipCurrent = ReferenceEquals(_workspace, _workspaces[0]);
            var emojiShown = await EventuallyAsync(() => HomeView().Mark is { Visibility: Visibility.Visible, Text: "🍁" } && HomeView().Glyph.Visibility == Visibility.Collapsed && FolderRootMarkOf(data) is { Visibility: Visibility.Visible, Text: "🍁" })
                && terminalHost.Mark.Visibility == Visibility.Collapsed && pane.FolderMarkText == "" && (!chipCurrent || (_workspaceChipMark is { Visibility: Visibility.Visible, Text: "🍁" } && _workspaceChipGlyph.Visibility == Visibility.Collapsed))
                && SavedPreferences(() => new AppPreferences()).FolderEmoji.GetValueOrDefault(pinnedFolder) == "🍁";
            SetFolderEmoji(data, "ab");
            var emojiCleared = HomeView().Mark.Visibility == Visibility.Collapsed && HomeView().Glyph.Visibility == Visibility.Visible && (!chipCurrent || _workspaceChipMark.Visibility == Visibility.Collapsed) && !_preferences.FolderEmoji.ContainsKey(pinnedFolder);
            SetFolderEmoji(data, "🍁");
            // The rows with the mark on them; captured for the eye.
            var emojiButton = HomeView().Mark.Text == "🍁";
            var folderCardImage = await CaptureShellAsync(Path.Combine(data, "workspace-row-native.png"), terminal);
            var folderEmoji = emojiShown && emojiCleared && emojiButton && terminalHost.Mark.Visibility == Visibility.Collapsed;
            if (!folderEmoji) throw new InvalidOperationException($"The folder emoji did not show on the rows and the indicator and stay off the tab, or a two-character mark was not refused: shown={emojiShown} cleared={emojiCleared} row={emojiButton} tab='{terminalHost.Mark.Text}' pane='{pane.FolderMarkText}'.");
            // Emoji… on the workspace's menu opens Windows' emoji panel over a box that is never seen (the smoke focuses it without
            // sending the chord): it takes the focus, an emoji typed into it is the home's at once, and it is emptied for the next one.
            await OpenEmojiPanelAsync(data);
            var emojiBoxFocused = _folderEmojiBox.FocusState != FocusState.Unfocused && _folderEmojiBox.Opacity == 0;
            _folderEmojiBox.Text = "🍂";
            // The box raises its change, and its loss of the focus, after the call that caused them.
            var emojiPicked = await EventuallyAsync(() => _preferences.FolderEmoji.GetValueOrDefault(pinnedFolder) == "🍂" && _folderEmojiBox.Text == "" && HomeView().Mark.Text == "🍂");
            _railItems["folders"].Button.Focus(FocusState.Programmatic);
            var emojiBoxLeft = await EventuallyAsync(() => !_folderEmojiBox.IsTabStop);
            SetFolderEmoji(data, "🍁");
            if (!emojiBoxFocused || !emojiPicked || !emojiBoxLeft) throw new InvalidOperationException($"The folder emoji's unseen box did not take the panel's emoji: focused={emojiBoxFocused} picked={emojiPicked} left={emojiBoxLeft} saved='{_preferences.FolderEmoji.GetValueOrDefault(pinnedFolder)}' box='{_folderEmojiBox.Text}' row='{HomeView().Mark.Text}' selected='{_selectedFolder}'.");
            // A color for the data folder (docs/decisions/folder-colors.md), through the menu's code path: the indicator is tinted with it
            // (a wash over the strip, outlined in it) while that workspace shows, and the live terminal's tab, working in the folder, does not (tabs
            // stopped wearing folder colors on November 8, 2026); a tab's own color stays its own. The folder's color is cleared after, and the tab's own put back.
            var ownColor = terminalHost.Saved.Color;
            SetTabColor(terminalHost, null);
            SetFolderColor(data, "green");
            var greenHex = TabColors.Resolve("green", _theme.StripDark)!;
            // The switcher's row for the workspace is tinted too, and only while it has a color: a row is built as the list opens.
            var colorHome = _workspaces.First(workspace => workspace.Home is { } home && PinnedFolders.Normalize(home) == pinnedFolder);
            bool SwitcherRowTinted()
            {
                var probe = new MenuFlyoutItem();
                TintSwitcherRow(probe, colorHome);
                return probe.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: > 0 };
            }
            // The inactive tabs on a colored strip are a darker shade of it, and clear on the theme's own.
            bool InactiveTabsShaded(bool tinted) => HostsOf(_workspace).Where(host => !ReferenceEquals(_tabs.SelectedItem, host.Item)).All(host => tinted
                ? StripTint is { } tint && host.Paint.Idle.Color == Theme.Over(Theme.WithAlpha(Microsoft.UI.Colors.Black, InactiveShade), tint) : host.Paint.Idle.Color.A == 0);
            var folderColorShown = SavedPreferences(() => new AppPreferences()).FolderColor.GetValueOrDefault(pinnedFolder) == "green" && WorkspaceColor(colorHome) == "green" && SwitcherRowTinted()
                && (!chipCurrent || (WorkspaceChipPaint.Strip == Theme.Over(Theme.WithAlpha(Theme.Parse(greenHex), StripWash), _theme.StripSurface) && WorkspaceChipPaint.Fill.A == 0
                    && _workspaceChipText.FontWeight.Weight == Microsoft.UI.Text.FontWeights.SemiBold.Weight && InactiveTabsShaded(true)));
            // The indicator is a flat block the strip's full height.
            _workspaceChip.UpdateLayout();
            var chipBox = _workspaceChip.Visibility == Visibility.Visible ? _workspaceChip.TransformToVisual(_tabs).TransformBounds(new Windows.Foundation.Rect(0, 0, _workspaceChip.ActualWidth, _workspaceChip.ActualHeight)) : (Windows.Foundation.Rect?)null;
            var chipImage = Path.Combine(data, "workspace-strip.png");
            await CaptureAsync(_root, chipImage);
            var chipFlat = chipBox is not { } box || (Math.Abs(box.Height - StripHeightNow) < 1 && Math.Abs(box.Top - StripBand.Top) < 1
                && _workspaceChip.BorderThickness == new Thickness(0) && _workspaceChip.CornerRadius == new CornerRadius(0)
                && WorkspaceChipFace is { Corner.TopLeft: 6, Height: TabContentHeight, ChevronUp: false, Divider: true });
            if (!chipFlat) throw new InvalidOperationException($"The workspace indicator does not fill the strip's height: {chipBox}, strip {StripHeightNow}; see {chipImage}.");
            var tabUncolored = EffectiveColor(terminalHost.Saved) is null && terminalHost.Paint.Bar.Visibility == Visibility.Collapsed;
            SetTabColor(terminalHost, "orange");
            var ownKept = EffectiveColor(terminalHost.Saved) == "orange" && terminalHost.Paint.Bar.Visibility == Visibility.Visible;
            SetFolderColor(data, null);
            var folderCleared = !_preferences.FolderColor.ContainsKey(pinnedFolder) && WorkspaceColor(colorHome) is null && !SwitcherRowTinted() &&(!chipCurrent || (WorkspaceChipPaint.Fill.A == 0 && ReferenceEquals(_stripBackdrop.Background, _theme.Rail) && InactiveTabsShaded(false) && WorkspaceChipPaint.Ink == _theme.StripTextSecondary.Color)) && EffectiveColor(terminalHost.Saved) == "orange";
            SetTabColor(terminalHost, ownColor);
            var folderColors = folderColorShown && tabUncolored && ownKept && folderCleared;
            if (!folderColors) throw new InvalidOperationException($"The folder's color was not shown on the indicator and kept off the tab: shown={folderColorShown} tab={tabUncolored} own={ownKept} cleared={folderCleared}.");
            // The folder's color on its rows (November 12, 2026): the listed folder's row in the Folders panel and its workspace's
            // row in Agent Workspaces have the folder's name and its folder glyph in the color, the one its swatch shows, and the
            // workspace's bar wears it too; nothing is filled (a wash over the row and a box around the chevron were both too
            // much). Without a color the name and the glyph have the theme's inks, and the bar is the accent's.
            SetFolderColor(data, "green");
            var colorRowView = _workspaceRows.GetValueOrDefault(colorHome.Id);
            var colorFolderView = _folderRows.GetValueOrDefault(PinnedFolders.Normalize(data));
            var swatchShade = Theme.Parse(TabColors.Resolve("green", _theme.StripDark)!);
            bool Inked(Microsoft.UI.Xaml.Media.Brush brush) => RowColor(data) is { } shade && brush is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 255 } ink && ink.Color == shade;
            bool Unfilled(Grid row) => row.Background is null || ReferenceEquals(row.Background, _theme.Transparent) || ReferenceEquals(row.Background, _theme.Hover) || ReferenceEquals(row.Background, _theme.Layer);
            var rowsInked = colorRowView is not null && colorFolderView is not null && (_theme.Light || RowColor(data) == swatchShade)
                && await EventuallyAsync(() => Inked(colorRowView!.Name.Foreground) && Inked(colorRowView.Glyph.Foreground) && Inked(colorFolderView!.Name.Foreground) && Inked(colorFolderView.Glyph.Foreground)
                    && Unfilled(colorRowView.Row) && Unfilled(colorFolderView.Row) && Inked(colorRowView.Bar.Background));
            SetFolderColor(data, null);
            var rowsPlain = colorRowView is not null && colorFolderView is not null && RowColor(data) is null && await EventuallyAsync(() => ReferenceEquals(colorRowView!.Bar.Background, _theme.Accent)
                && (ReferenceEquals(colorRowView.Name.Foreground, _theme.Text) || ReferenceEquals(colorRowView.Name.Foreground, _theme.TextSecondary)) && ReferenceEquals(colorRowView.Glyph.Foreground, _theme.TextSecondary)
                && ReferenceEquals(colorFolderView!.Name.Foreground, _theme.Text) && ReferenceEquals(colorFolderView.Glyph.Foreground, _theme.TextSecondary));
            if (!rowsInked || !rowsPlain) throw new InvalidOperationException($"A folder's color did not show as the ink of its name and glyph on its rows in the panels, or stayed once cleared: inked={rowsInked} plain={rowsPlain}.");
            // A theme's own color (November 12, 2026): kept as its id, painted in the current theme's shade of it, the one its
            // swatch shows, and not remembered as a custom color.
            var recentBeforeThemeColor = _preferences.RecentTabColors.Count;
            SetFolderColor(data, "theme-blue");
            var themeColorKept = WorkspaceColor(colorHome) == "theme-blue" && ResolveColor("theme-blue", _theme.StripDark) == _theme.Definition.Terminal.Blue.ToUpperInvariant()
                && _preferences.RecentTabColors.Count == recentBeforeThemeColor && TabColors.ThemeRoles.All(role => TabColors.ThemeShade(role.Id, _theme.Definition) is { Length: 6 })
                && await EventuallyAsync(() => colorRowView is not null && colorRowView.Bar.Background is Microsoft.UI.Xaml.Media.SolidColorBrush themeBar && themeBar.Color == RowColor(data));
            SetFolderColor(data, null);
            if (!themeColorKept) throw new InvalidOperationException("A theme's own color given to a folder was not kept as its id, painted in the theme's shade, or was remembered as a custom color.");
            // Completion sounds (docs/decisions/completion-sounds.md): the built-in recipes list first; a WAV file dropped in the data
            // folder's sounds folder is listed by its name after a Reload, cut to the longest length when it is longer, and a broken
            // file is named with its error and not listed; the home folder's sound (Sound… on the workspace's menu, whose flyout
            // lists it) rings for a turn in that folder and the sound chosen in Settings for one elsewhere; both are left set for the restore stage.
            var soundsFolder = SoundsFolder;
            Directory.CreateDirectory(soundsFolder);
            File.WriteAllBytes(Path.Combine(soundsFolder, "long-tone.wav"), SmokeWav(3.0));
            File.WriteAllText(Path.Combine(soundsFolder, "broken.wav"), "not a wave");
            var soundProblems = ReloadSounds(notify: false);
            var longTone = SoundCatalog.Find("long-tone.wav");
            var soundListed = SoundCatalog.BuiltIn.Count == 21 && SoundCatalog.All.Count == 22 && !longTone.BuiltIn && longTone.Cut && Math.Abs(longTone.Seconds - SoundCatalog.MaxSeconds) < 0.001
                && WavImage.Samples(longTone.Image).Max(sample => Math.Abs((int)sample)) / 32767.0 is > 0.29 and < 0.31
                && _soundPicker.Items.OfType<ComboBoxItem>().Any(item => item.Tag as string == "long-tone.wav" && item.Content is string label && label.Contains("cut to", StringComparison.Ordinal))
                && soundProblems.Count == 1 && soundProblems[0].File == "broken.wav" && _soundProblems.Visibility == Visibility.Visible;
            SetSound("long-tone.wav");
            SetFolderSound(data, "ding");
            ShowFolderSoundFlyout(HomeView().Row, data);
            var folderSoundButton = await EventuallyAsync(() => _folderSoundPicker.SelectedItem is ComboBoxItem { Tag: "ding" } && _folderSoundTitle.Text == "Sound for " + Paths.Leaf(data));
            _folderSoundFlyout?.Hide();
            _lastChime = null;
            RingReadyChime(data);
            var folderSoundRang = LastSoundRung == "ding";
            _lastChime = null;
            RingReadyChime(Path.GetTempPath());
            var chosenSoundRang = LastSoundRung == "long-tone.wav";
            var savedSounds = SavedPreferences(() => new AppPreferences());
            var soundSaved = Sound.SameId(savedSounds.Sound, "long-tone.wav") && savedSounds.FolderSound.GetValueOrDefault(pinnedFolder) == "ding";
            var sounds = soundListed && folderSoundButton && folderSoundRang && chosenSoundRang && soundSaved;
            if (!sounds) throw new InvalidOperationException($"The sounds were not listed, cut, refused, chosen or rung as expected: listed={soundListed} button={folderSoundButton} folder rang={folderSoundRang} chosen rang={chosenSoundRang} saved={soundSaved} problems={soundProblems.Count}.");
            // A folder row carries its name and its badge (hidden while the folder has no open session) and no button: Open in File
            // Explorer sat at the row's edge until November 12, 2026. Ctrl+Shift+E opens the folder last clicked in a panel, a folder
            // of the Folders panel or a workspace's row, and the focused pane's again once that choice is let go. The folder menu's
            // item opens the folder, and a folder gone since it was listed says so instead of Explorer opening its default folder.
            // The smoke records where each would go rather than open windows on the desktop.
            static bool RowIsClean(TreeViewNode node) => FolderRowView(node) is Grid row && !row.Children.OfType<Button>().Any()
                && row.ColumnDefinitions.Count == 2 && row.Children.OfType<Border>().Single() is { Visibility: Visibility.Collapsed };
            var explorerPlaced = await EventuallyAsync(() => HomeTree().RootNodes.Count > 0 && RowIsClean(HomeTree().RootNodes[0]));
            var chosenRowFolder = _folderPaths[HomeTree().RootNodes[0]];
            SelectFolder(chosenRowFolder);
            LastExplorerFolder = null;
            await HandleShortcutAsync("open-folder");
            var explorerFromRow = LastExplorerFolder == chosenRowFolder;
            ForgetSelectedFolder();
            LastExplorerFolder = null;
            await HandleShortcutAsync("open-folder");
            explorerFromRow = explorerFromRow && LastExplorerFolder != chosenRowFolder
                && LastExplorerFolder == (SelectedHost is { } chosenKeyHost ? FocusedPane(chosenKeyHost).FolderToOpen : _workspace?.Home);
            void Press(Button button) => new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button).Invoke();
            LastExplorerFolder = null;
            OpenInExplorer(data);
            var explorerFromCard = LastExplorerFolder == data;
            LastExplorerFolder = null;
            OpenInExplorer(Path.Combine(data, "gone-" + Guid.NewGuid().ToString("N")));
            var explorerGoneRefused = LastExplorerFolder is null && LastNotice is { Severity: InfoBarSeverity.Error };
            _notice.IsOpen = false;
            var folderExplorer = explorerPlaced && explorerFromRow && explorerFromCard && explorerGoneRefused;
            if (!folderExplorer) throw new InvalidOperationException($"A folder row had a button, or Ctrl+Shift+E did not open the folder chosen and then the pane's, or the listed folder did not open, or opened a folder that had gone: placed={explorerPlaced} row={explorerFromRow} home={explorerFromCard} goneRefused={explorerGoneRefused}.");
            // A folder expanded, collapsed and expanded again stays open and shows every child row each time: the first expand
            // once closed itself as the children were listed, and a collapse left the rows in their old containers, so every
            // other expand showed them blank. The six sit in a folder of the home, so a row of the tree has them as children.
            var treeFolder = Path.Combine(data, "tree");
            foreach (var name in new[] { "tree-a", "tree-b", "tree-c", "tree-d", "tree-e", "tree-f" }) Directory.CreateDirectory(Path.Combine(treeFolder, name));
            await RelistFolderTreeAsync(data);
            var blankOn = new List<string>();
            foreach (var (tree, section) in new[] { (HomeTree(), "Folders") })
            {
                var expandRoot = tree.RootNodes.First(node => _folderPaths.GetValueOrDefault(node) == treeFolder);
                for (var round = 1; round <= 4; round++)
                {
                    expandRoot.IsExpanded = true;
                    if (!await EventuallyAsync(() => expandRoot.IsExpanded && expandRoot.Children.Count >= 6 && expandRoot.Children.All(child => RowPresented(tree, child))))
                        blankOn.Add($"{section} expand {round}: {expandRoot.Children.Count(child => !RowPresented(tree, child))} of {expandRoot.Children.Count} blank, expanded={expandRoot.IsExpanded}");
                    expandRoot.IsExpanded = false;
                    await EventuallyAsync(() => expandRoot.Children.All(child => tree.ContainerFromNode(child) is null));
                    // Every other round lets the collapse lay out first, as a click would; the rest expand again in the same frame.
                    if (round % 2 == 1) await Task.Delay(250);
                }
            }
            var folderRowsPresented = blankOn.Count == 0;
            if (!folderRowsPresented) throw new InvalidOperationException("Re-expanding a folder left child rows blank: " + string.Join("; ", blankOn) + ".");
            // New folder (a folder's menu, November 12, 2026): made in a folder of a tree, the folder opens with the new row in
            // it; made in the listed folder, its tree has the row; a name taken or one Windows refuses makes nothing. The
            // fixture folders are left as they were.
            var newFolderParent = Path.Combine(treeFolder, "tree-f");
            var newFolderNode = _folderPaths.First(entry => entry.Value == newFolderParent).Key;
            var madeInTree = await CreateFolderAsync(newFolderParent, "  made here ");
            var madeInTreeShown = madeInTree == Path.Combine(newFolderParent, "made here") && Directory.Exists(madeInTree)
                && await EventuallyAsync(() => newFolderNode.IsExpanded && newFolderNode.Children.Any(child => _folderPaths.GetValueOrDefault(child) == madeInTree));
            var takenRefused = await CreateFolderAsync(newFolderParent, "made here") is null && await CreateFolderAsync(newFolderParent, "a?b") is null && await CreateFolderAsync(newFolderParent, "nul") is null
                && Directory.GetFileSystemEntries(newFolderParent).Length == 1;
            var madeInRoot = await CreateFolderAsync(data, "made in root");
            var madeInRootShown = madeInRoot is not null && Directory.Exists(madeInRoot) && await EventuallyAsync(() => HomeTree().RootNodes.Any(node => _folderPaths.GetValueOrDefault(node) == madeInRoot));
            var newFolderItems = FolderRowOf(data)?.ContextFlyout is MenuFlyout && NewFolderItem(data).Text == "New folder…";
            newFolderNode.IsExpanded = false;
            if (madeInTree is not null) Directory.Delete(madeInTree);
            if (madeInRoot is not null) Directory.Delete(madeInRoot);
            await RelistFolderTreeAsync(data);
            if (!(madeInTreeShown && takenRefused && madeInRootShown && newFolderItems))
                throw new InvalidOperationException($"New folder did not make and show a folder, or made one it should have refused: tree={madeInTreeShown} refused={takenRefused} root={madeInRootShown} item={newFolderItems}.");
            // The listing keeps up with the disk (since October 14, 2026; before, a folder was listed once per run): a folder made after
            // its parent was listed shows at the parent's next expand, and with the parent left open, when the Folders panel comes
            // into view (the window coming back to the front reads it the same way; the listed folder is read first, then its open rows); a
            // subfolder found empty lists a folder made in it once its parent has been read again; an open subfolder stays open, the
            // same row, through its parent's re-reads; and a folder deleted goes. The fixture folders are left as they were.
            var relistRoot = HomeTree().RootNodes.First(node => _folderPaths.GetValueOrDefault(node) == treeFolder);
            TreeViewNode? RowOf(TreeViewNode parent, string path) => parent.Children.FirstOrDefault(child => _folderPaths.GetValueOrDefault(child) == path);
            var madeLater = Path.Combine(treeFolder, "tree-g");
            Directory.CreateDirectory(madeLater);
            relistRoot.IsExpanded = true;
            var relistedOnExpand = await EventuallyAsync(() => RowOf(relistRoot, madeLater) is not null && RowOf(relistRoot, Path.Combine(treeFolder, "tree-b")) is not null);
            var emptyRow = RowOf(relistRoot, Path.Combine(treeFolder, "tree-b"));
            var innerFolder = Path.Combine(treeFolder, "tree-b", "inner");
            var madeWhileOpen = Path.Combine(treeFolder, "tree-h");
            var relistedOnPanel = false; var innerListed = false; var openKept = false; var deletedGone = false;
            if (emptyRow is not null)
            {
                emptyRow.IsExpanded = true;
                await EventuallyAsync(() => !emptyRow.HasUnrealizedChildren);
                Directory.CreateDirectory(innerFolder);
                Directory.CreateDirectory(madeWhileOpen);
                OpenPanel("folders");
                relistedOnPanel = await EventuallyAsync(() => RowOf(relistRoot, madeWhileOpen) is not null);
                // Still open, the subfolder is read with its parent; shut, it has its chevron back and its own expand reads it.
                await EventuallyAsync(() => RowOf(emptyRow, innerFolder) is not null || emptyRow is { IsExpanded: false, HasUnrealizedChildren: true });
                if (!emptyRow.IsExpanded) emptyRow.IsExpanded = true;
                innerListed = await EventuallyAsync(() => RowOf(emptyRow, innerFolder) is not null);
                relistRoot.IsExpanded = false;
                await Task.Delay(250);
                relistRoot.IsExpanded = true;
                openKept = await EventuallyAsync(() => ReferenceEquals(RowOf(relistRoot, Path.Combine(treeFolder, "tree-b")), emptyRow) && emptyRow.IsExpanded && RowOf(emptyRow, innerFolder) is not null);
                Directory.Delete(madeLater); Directory.Delete(madeWhileOpen); Directory.Delete(innerFolder);
                OpenPanel("folders");
                deletedGone = await EventuallyAsync(() => RowOf(relistRoot, madeLater) is null && RowOf(relistRoot, madeWhileOpen) is null && RowOf(emptyRow, innerFolder) is null);
            }
            relistRoot.IsExpanded = false;
            var foldersRelisted = relistedOnExpand && relistedOnPanel && innerListed && openKept && deletedGone;
            if (!foldersRelisted)
                throw new InvalidOperationException($"A folder's listing did not keep up with the disk: on expand={relistedOnExpand} on panel={relistedOnPanel} inner={innerListed} open kept={openKept} deleted gone={deletedGone}.");
            // Add to Folders on a folder row: the folder joins the list however the path is written, with a row and an empty tree in the
            // Folders panel and a workspace named after it, and is saved; the strip stays on the first workspace. Folded, it is folded;
            // removed, it goes, its row, its tree and its fold with it, and the folder is not listed. The list is left as it was.
            var addedFolder = Path.Combine(treeFolder, "tree-a");
            var foldersBeforeAdd = Folders.Count;
            var madeHere = AddFolder(addedFolder.ToUpperInvariant() + "\\");
            var savedAfterAdd = SavedPreferences(() => new AppPreferences());
            var workspaceAdded = Folders.Count == foldersBeforeAdd + 1 && IsFolder(addedFolder) && PinnedFolders.Same(madeHere.Home, addedFolder) && string.Equals(WorkspaceName(madeHere), "tree-a", StringComparison.OrdinalIgnoreCase)
                && _workspaceRows.ContainsKey(madeHere.Id) && ReferenceEquals(_workspace, _workspaces[0]) && savedAfterAdd.Folders is { } savedList && savedList.Any(saved => PinnedFolders.Same(saved, addedFolder))
                && SavedLayout(() => new WorkspaceLayout()).Workspaces is { } savedWithFolder && savedWithFolder.Any(saved => saved.Id == madeHere.Id && PinnedFolders.Same(saved.Home, addedFolder));
            var addedTree = await EventuallyAsync(() => FolderRowOf(addedFolder) is { ActualWidth: > 0 } && FolderTreeOf(addedFolder) is { Visibility: Visibility.Visible } addedTreeView && addedTreeView.RootNodes.Count == 0 && _folderTreesListed.Contains(PinnedFolders.Normalize(addedFolder)));
            ToggleFolderFold(addedFolder);
            var addedFolded = FolderFolded(addedFolder) && FolderTreeOf(addedFolder) is { Visibility: Visibility.Collapsed };
            RemoveFolder(addedFolder);
            var workspaceRemoved = Folders.Count == foldersBeforeAdd && !IsFolder(addedFolder) && !_workspaceRows.ContainsKey(madeHere.Id) && FolderTreeOf(addedFolder) is null && FolderRowOf(addedFolder) is null
                && !_preferences.FoldedSections.Contains(SectionFolds.Root(addedFolder)) && Folders.SequenceEqual([pinnedFolder]);
            var workspaceFromFolder = workspaceAdded && addedTree && addedFolded && workspaceRemoved;
            if (!workspaceFromFolder)
                throw new InvalidOperationException($"Add to Folders did not list, name, save, fold and remove a folder with its workspace: added={workspaceAdded} tree={addedTree} folded={addedFolded} removed={workspaceRemoved} folders=[{string.Join("; ", Folders)}].");
            // Settings → Updates in a copy run from a build folder: not installed, so it says so and cannot check.
            // A copy the MSI installed for every user (run from Program Files, where this user cannot write) says so instead, and cannot check either.
            var updatesNotInstalled = (_updates is { IsInstalled: false } && _updateStatus.Text.StartsWith("This copy is not installed", StringComparison.Ordinal)
                    || _updates is { IsManaged: true, UpdatesItself: false } && _updateStatus.Text.Contains("Installed for every user of this PC", StringComparison.Ordinal))
                && !_checkUpdates.IsEnabled && _restartToUpdate.Visibility == Visibility.Collapsed;
            var updatesSection = updatesNotInstalled;
            if (!updatesSection)
                throw new InvalidOperationException($"Settings → Updates did not show a build-folder copy as not installed: notInstalled={updatesNotInstalled} status={_updateStatus.Text}");
            // The app icon: the shipped one's .ico files, for a light and a dark taskbar, load at the taskbar's and the tray's
            // sizes; it is on the taskbar button, the title bar and the tray in the file for the taskbar's tone; and the window
            // carries no identity of its own. Settings → App icon offered the design rounds' candidates up to October 8, 2026,
            // so a trial word is written to the settings file here, and the restore stage shows that a saved choice is dropped.
            var shipped = AppIcon.Find(AppIcon.Default);
            var appIconFiles = new[] { false, true }.All(light =>
            {
                var name = AppIcon.IconFor(shipped, light);
                var buttonFrame = ShellIcons.Load(BrandFile(name + ".ico"), ShellIcons.TaskbarButtonSize());
                var trayFrame = ShellIcons.Load(BrandFile(name + ".ico"), ShellIcons.TraySize());
                var loaded = buttonFrame != 0 && trayFrame != 0;
                ShellIcons.Destroy(buttonFrame); ShellIcons.Destroy(trayFrame);
                return loaded;
            });
            var shippedPath = BrandFile(AppIcon.IconFor(shipped, ShellIcons.LightTaskbar()) + ".ico");
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var appIconShipped = appIconFiles && string.Equals(_appIconPath, shippedPath, StringComparison.OrdinalIgnoreCase) && _taskbarIcon != 0 && _titleBarIcon != 0
                && _tray is { IconSize: > 0 } appIconTray && string.Equals(appIconTray.IconPath, shippedPath, StringComparison.OrdinalIgnoreCase)
                && WindowIdentity.Get(windowHandle) is null && WindowIdentityId is null;
            if (!appIconShipped)
                throw new InvalidOperationException($"The shipped app icon did not load for both taskbar tones or is not on the shell: files={appIconFiles} path={_appIconPath} tray={_tray?.IconPath} windowId={WindowIdentity.Get(windowHandle)}.");
            _preferences.AppIcon = "lit-hub";
            SavePreferences(); FlushState();
            // Choosing a theme in Settings leaves the panel where it was: a theme change once left it scrolled to its end. The
            // theme is chosen the way a click chooses it, through its row with the focus on it, and then put back.
            OpenPanel("settings");
            // Five tabs along the panel's bottom, each showing its own page from the top, the one shown saved for next time.
            var tabPages = new List<string>();
            foreach (var tab in SettingsTabs.Choices.Reverse())
            {
                ShowSettingsTab(tab);
                await Task.Delay(150);
                await CaptureAsync(_root, Path.Combine(data, "settings-" + tab + "-native.png"));
                tabPages.Add(tab + ":" + (ReferenceEquals(_settingsScroll.Content, _settingsTabs[tab].Page) && _settingsTabs[tab].Pill.Visibility == Visibility.Visible
                    && _settingsTabs.Count(pair => pair.Value.Pill.Visibility == Visibility.Visible) == 1 && _preferences.SettingsTab == tab
                    && SavedPreferences(() => new AppPreferences()).SettingsTab == tab && _settingsTabs[tab].Button.ActualHeight > 40));
            }
            // The checks from here on that take the keyboard to a control (the indicator's slider, the theme list) find it on
            // Appearance, which was General's first section when they were written.
            ShowSettingsTab(SettingsTabs.Appearance);
            await Task.Delay(150);
            var agentsPageHasEveryAgent = _agentRows.Count == _preferences.Agents.Count && _preferences.Agents.All(agent => _agentRows[agent.Id].Switch.IsOn == agent.IsOn);
            var aboutPageHasUpdates = _settingsTabs[SettingsTabs.About].Page.Children.Count > 4;
            // An agent's switch, as the card's toggle sets it: saved at once, and the card follows.
            bool? SavedSwitch(string id) => SavedPreferences(() => new AppPreferences()).Agents.Single(agent => agent.Id == id).Enabled;
            SetAgentEnabled("opencode", false);
            var switchedOff = !_agentRows["opencode"].Switch.IsOn && SavedSwitch("opencode") == false && _agentRows["opencode"].Identity.Opacity < 1;
            _agentRows["opencode"].Switch.IsOn = true;
            var switchedOn = SavedSwitch("opencode") == true && _agentRows["opencode"].Identity.Opacity == 1;
            if (tabPages.Any(page => page.EndsWith(":False", StringComparison.Ordinal)) || !agentsPageHasEveryAgent || !aboutPageHasUpdates || !switchedOff || !switchedOn)
                throw new InvalidOperationException($"The Settings tabs did not each show their page: {string.Join(", ", tabPages)} agents={agentsPageHasEveryAgent} about={aboutPageHasUpdates} switch off={switchedOff} on={switchedOn}.");
            // The setup notice (MainWindow.Setup.cs; November 27, 2026), on gaps this run stands in for, since the smoke's PC has
            // PowerShell 7 and the fixture agents: at launch the combined message with its five links, and the same in Settings →
            // Agents; a gap that goes leaves both; the launch X closes its gaps at launch only, the Settings X in Settings only,
            // each gap apart; with nothing missing the notice goes without closing anything; and the next notice drops the links.
            var setupClosedBefore = (Launch: _preferences.SetupClosedAtLaunch, Settings: _preferences.SetupClosedInSettings);
            _notice.IsOpen = false; LastNotice = null;
            _preferences.SetupClosedAtLaunch = _preferences.SetupClosedInSettings = SetupGap.None;
            bool setupNotice;
            string setupImage = Path.Combine(data, "setup-notice-native.png");
            try
            {
                string Said(SetupGap gaps) => SetupAdvisor.For(gaps)!.Message;
                SetupGapsOverride = SetupGap.Both;
                ShowSetupNotice();
                var setupBoth = SetupNoticeShown && _notice.Message == Said(SetupGap.Both) && _notice.Title == ""
                    && SetupLinkNames(_notice.Content).SequenceEqual(["PowerShell 7", "Claude Code", "Codex", "OpenCode", "Hermes"]);
                ShowSettingsTab(SettingsTabs.Agents);
                RefreshSetupAdvice();
                var setupBarBoth = await EventuallyAsync(() => SetupBar is { IsOpen: true, ActualHeight: > 0 } bar && bar.Message == Said(SetupGap.Both) && SetupLinkNames(bar.Content).Count == 5);
                var setupBar = SetupBar!;
                await Task.Delay(150);
                await CaptureAsync(_root, setupImage);
                // An agent found: both places say PowerShell alone.
                SetupGapsOverride = SetupGap.PowerShell;
                RefreshSetupAdvice();
                var setupFollowed = SetupNoticeShown && _notice.Message == Said(SetupGap.PowerShell) && SetupLinkNames(_notice.Content).SequenceEqual(["PowerShell 7"])
                    && setupBar.Message == Said(SetupGap.PowerShell);
                // The launch X: not at launch again, still in Settings.
                CloseSetupNotice();
                _notice.IsOpen = false;
                ShowSetupNotice();
                var setupClosedAtLaunch = !_notice.IsOpen && _preferences.SetupClosedAtLaunch == SetupGap.PowerShell && setupBar.IsOpen
                    && SavedPreferences(() => new AppPreferences()).SetupClosedAtLaunch == SetupGap.PowerShell;
                // The agent gap again: at launch alone, in Settings with PowerShell; then the Settings X closes both there.
                SetupGapsOverride = SetupGap.Both;
                ShowSetupNotice();
                RefreshSetupAdvice();
                var setupAgentAlone = SetupNoticeShown && _notice.Message == Said(SetupGap.Agent) && setupBar.IsOpen && setupBar.Message == Said(SetupGap.Both);
                CloseSetupBar();
                RefreshSetupAdvice();
                var setupClosedInSettings = !setupBar.IsOpen && _preferences.SetupClosedInSettings == SetupGap.Both
                    && SavedPreferences(() => new AppPreferences()).SetupClosedInSettings == SetupGap.Both;
                // Nothing missing: the notice goes, and nothing more is closed at launch.
                SetupGapsOverride = SetupGap.None;
                RefreshSetupAdvice();
                var setupGoneWithGap = !_notice.IsOpen && _preferences.SetupClosedAtLaunch == SetupGap.PowerShell;
                NotifyBriefly("Smoke", "A plain notice.");
                var setupLinksDropped = _notice.Content is null;
                _notice.IsOpen = false; LastNotice = null;
                setupNotice = setupBoth && setupBarBoth && setupFollowed && setupClosedAtLaunch && setupAgentAlone && setupClosedInSettings && setupGoneWithGap && setupLinksDropped;
                if (!setupNotice) throw new InvalidOperationException($"The setup notice did not show, follow or close as laid out: both={setupBoth} bar={setupBarBoth} followed={setupFollowed} closedAtLaunch={setupClosedAtLaunch} agentAlone={setupAgentAlone} closedInSettings={setupClosedInSettings} gone={setupGoneWithGap} linksDropped={setupLinksDropped} message='{_notice.Message}' bar='{SetupBar?.Message}'.");
            }
            finally
            {
                SetupGapsOverride = null;
                (_preferences.SetupClosedAtLaunch, _preferences.SetupClosedInSettings) = setupClosedBefore;
                SavePreferences();
                RefreshSetupAdvice();
                ShowSettingsTab(SettingsTabs.Appearance);
                await Task.Delay(150);
            }
            // Settings → Session Indicator Width shows its width as it moves: while the slider has the keyboard, the pane that had
            // it wears the border even alone in its tab, and once the keyboard moves on to another setting, a lone pane wears none.
            var tabBeforeLone = _tabs.SelectedItem;
            var loneSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · lone pane", data, "");
            var loneHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), loneSaved.Title, [loneSaved]));
            var lonePane = loneHost.Panes[0];
            bool Ringed(SessionPane item) => item.RingLine.BorderBrush is Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var color } && color == _theme.Accent.Color;
            var loneBare = await EventuallyAsync(() => _activePane == lonePane) && !Ringed(lonePane);
            var sliderFocused = _paneBorderSlider!.Focus(FocusState.Keyboard);
            var borderShownAlone = sliderFocused && await EventuallyAsync(() => Ringed(lonePane));
            _themePicker.Focus(FocusState.Keyboard);
            var previewGone = await EventuallyAsync(() => !Ringed(lonePane));
            await CloseTabAsync(loneHost.Item);
            _tabs.SelectedItem = tabBeforeLone;
            _settingsScroll.ChangeView(null, 0, null, true);
            var paneBorderPreview = loneBare && borderShownAlone && previewGone;
            if (!paneBorderPreview)
                throw new InvalidOperationException($"Session Indicator Width's slider did not show the border on a lone pane while it had the keyboard, or left it there: bare={loneBare} focused={sliderFocused} shown={borderShownAlone} gone={previewGone}.");
            // Off everywhere: Claude Code, whose fixture conversations Sessions lists, switched off and on again. Its rows and
            // filter go, a placeholder of it is left out of Resume all, a new session of it is refused; on again, all come back.
            bool ClaudeListed() => _history.Items.OfType<ListViewItem>().Any(row => row.Tag is Conversation { AgentId: "claude" });
            var claudeListedBefore = await EventuallyAsync(ClaudeListed);
            SetAgentEnabled("claude", false);
            var claudeRowsGone = await EventuallyAsync(() => !ClaudeListed());
            var claudeFilterGone = _agentItems["claude"].Visibility == Visibility.Collapsed;
            var claudePlaceholdersOff = _open.Values.SelectMany(host => host.Panes.Select(pane => (host, pane)))
                .Where(item => item.pane.Descriptor.AgentId == "claude" && !item.pane.IsRunning)
                .All(item => StateOf(item.host, item.pane) == PaneStartState.AgentOff);
            if (_open.Values.FirstOrDefault(host => host.Panes.Any(pane => pane.Descriptor.AgentId == "claude" && pane.Terminal is null)) is { } offHost)
            {
                var selectedBefore = _tabs.SelectedItem;
                _tabs.SelectedItem = offHost.Item;
                await Task.Delay(300);
                await CaptureAsync(_root, Path.Combine(data, "agent-off-placeholder-native.png"));
                _tabs.SelectedItem = selectedBefore;
            }
            string? claudeStartRefused = null;
            try { await StartNewAsync("claude", data); }
            catch (InvalidOperationException refused) { claudeStartRefused = refused.Message; }
            SetAgentEnabled("claude", true);
            var claudeBack = await EventuallyAsync(ClaudeListed) && _agentItems["claude"].Visibility == Visibility.Visible;
            if (!claudeListedBefore || !claudeRowsGone || !claudeFilterGone || !claudePlaceholdersOff || claudeStartRefused?.Contains("is off in Settings", StringComparison.Ordinal) != true || !claudeBack)
                throw new InvalidOperationException($"Switching Claude Code off did not take it out of sight, or on did not bring it back: listed={claudeListedBefore} rowsGone={claudeRowsGone} filterGone={claudeFilterGone} placeholders={claudePlaceholdersOff} refused={claudeStartRefused} back={claudeBack}.");
            // An agent installed since detection last looked (October 19, 2026): undecided, OpenCode has no tile when New session
            // opens; the detection the dialog starts finds it, turns it on, says so, and adds its tile before PowerShell's while
            // the dialog is up. Windows PowerShell stands in for the install, since every PC that runs SicTerm has it.
            var openCodeBefore = _preferences.Agents.Single(agent => agent.Id == "opencode");
            _preferences.Agents = _preferences.Agents.Select(agent => agent.Id == "opencode" ? agent with { Enabled = null, Executable = "powershell" } : agent).ToList();
            RefreshAgentRows();
            _detectedAt = DateTimeOffset.MinValue;
            string[] TileIds() => _newSessionTiles?.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().Select(button => (string)button.Tag).ToArray() ?? [];
            var newSession = ShowNewSessionAsync(data);
            var dialogUp = await EventuallyAsync(() => _newSessionTiles is not null);
            var tilesAtOpen = TileIds();
            var foundDeadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < foundDeadline && !TileIds().Contains("opencode")) await Task.Delay(100);
            var tilesFound = TileIds();
            var foundTurnedOn = SavedSwitch("opencode") == true && LastNotice is { Brief: true, Title: "Agent turned on" } foundNotice && foundNotice.Message == "OpenCode detected on this PC.";
            _newSessionDialog?.Hide();
            await newSession;
            _preferences.Agents = _preferences.Agents.Select(agent => agent.Id == "opencode" ? openCodeBefore : agent).ToList();
            SavePreferences();
            await ApplyAgentSwitchesAsync(refreshHistory: false);
            var installedAgentFound = dialogUp && !tilesAtOpen.Contains("opencode") && tilesFound.Length == tilesAtOpen.Length + 1
                && tilesFound[^2] == "opencode" && tilesFound[^1] == "shell" && foundTurnedOn;
            if (!installedAgentFound)
                throw new InvalidOperationException($"New session did not find an agent installed since the last detection: up={dialogUp} at open={string.Join(",", tilesAtOpen)} after={string.Join(",", tilesFound)} on and noticed={foundTurnedOn} ({LastNotice?.Title}: {LastNotice?.Message}).");
            // The theme rows below are on Appearance, General's first section until November 12, 2026.
            ShowSettingsTab(SettingsTabs.Appearance);
            await Task.Delay(150);
            // Appearance alone may fit the panel with nothing to scroll; then the scroll is none, and the check is that it stays so.
            await EventuallyAsync(() => _settingsScroll.ScrollableHeight > 100);
            var themeScroll = _settingsScroll.ScrollableHeight > 100 ? 60d : 0d;
            _settingsScroll.ChangeView(null, themeScroll, null, true);
            await EventuallyAsync(() => Math.Abs(_settingsScroll.VerticalOffset - themeScroll) < 1);
            var themeBeforeScroll = _preferences.Theme;
            var otherTheme = _themeItems.Keys.First(id => id != themeBeforeScroll);
            _themePicker.Focus(FocusState.Pointer);
            _themePicker.SelectedItem = _themeItems[otherTheme];
            await Task.Delay(600);
            var offsetAfterTheme = _settingsScroll.VerticalOffset;
            var focusAfterTheme = FocusManager.GetFocusedElement(_root.XamlRoot) is FrameworkElement focusedAfterTheme ? focusedAfterTheme.GetType().Name : "none";
            var themeChanged = _preferences.Theme != themeBeforeScroll;
            ApplyTheme(themeBeforeScroll);
            var themeKeptScroll = themeChanged && Math.Abs(offsetAfterTheme - themeScroll) < 2 && _preferences.Theme == themeBeforeScroll;
            if (!themeKeptScroll)
                throw new InvalidOperationException($"Choosing a theme moved the Settings panel from {themeScroll} px to {offsetAfterTheme} px of {_settingsScroll.ScrollableHeight} (theme changed: {themeChanged}, focus then: {focusAfterTheme}).");
            OpenPanel("workspaces");
            OpenPanel("sessions");
            if (!panelCollapsed || !panelExpanded || !workspacesPanel) throw new InvalidOperationException("Collapsing, restoring, or switching the panel did not preserve the live terminal.");
            if (!panelSlid) throw new InvalidOperationException("The panel did not slide closed in its own time: " + slide.ElapsedMilliseconds + " ms.");
            if (!motionSetting) throw new InvalidOperationException($"Settings → Menu Animation did not decide the slide: slid={panelSlid} snapped={panelSnapped} ({snap.ElapsedMilliseconds} ms) motion={_preferences.Motion}.");
            var panePathFits = splitPathsFit && collapsedPathFits && pane.PathFitsBest();
            if (!panePathFits) throw new InvalidOperationException("A pane header did not show its whole working folder when it fit, or did not keep the folder name when it had to shorten.");

            // Where a drag of the panel's edge ends, set the way its release sets it: the slot and the body take the width
            // together, the terminal beside it gives up columns, the preference is saved, and both bounds hold. 400 px needs
            // the fixed 1440 px window at 150 % display scaling or less, or the terminal-area floor clamps it. The upper bound is
            // the panel's maximum, or less where the terminal beside it would drop under its floor: the fixed window is 1440
            // pixels, 960 DIPs at 150 %, which leaves the panel about 510.
            var panelBefore = (Slot: _panel.Width, Terminal: terminal.ActualWidth);
            var beforeWiden = await terminal.GetDimensionsAsync();
            var widened = SetPanelWidth(SmokePanelWidth);
            await WaitForAsync(async () => (await terminal.GetDimensionsAsync()).Columns < beforeWiden.Columns);
            var panelWidthApplied = widened == SmokePanelWidth && _panel.Width == SmokePanelWidth && _panelBody.Width == SmokePanelWidth && _panelBody.Margin.Left == 0
                && _panelEdge.Visibility == Visibility.Visible && SavedPreferences(() => new AppPreferences()).PanelWidth == SmokePanelWidth;
            var widest = Math.Min(PanelLayout.MaxWidth, (int)Math.Floor(_root.ActualWidth - RailWidth) - PanelLayout.MinTerminalWidth);
            var panelWidthClamped = SetPanelWidth(5000) == widest && _panel.Width == widest
                && SetPanelWidth(10) == PanelLayout.MinWidth && _panel.Width == PanelLayout.MinWidth;
            // Back to the default before anything captures or compares widths.
            SetPanelWidth(PanelLayout.DefaultWidth);
            var returned = await WaitForSizeAsync(terminal, size => size.Columns == beforeWiden.Columns);
            if (returned.Columns != beforeWiden.Columns)
                throw new InvalidOperationException($"Back at the default panel width the terminal had {returned.Columns} columns, not the {beforeWiden.Columns} it had before the panel widened: "
                    + $"slot {panelBefore.Slot} then {_panel.Width}, terminal {panelBefore.Terminal} then {terminal.ActualWidth} DIPs, body={_panelBody.Width} sliding={_panelMotion is not null} root={_root.ActualWidth} scale={_root.XamlRoot?.RasterizationScale}.");
            if (!panelWidthApplied || !panelWidthClamped)
                throw new InvalidOperationException($"Setting the panel width did not resize the panel and the terminal and save it, or a bound did not hold: applied={panelWidthApplied} clamped={panelWidthClamped} root={_root.ActualWidth} scale={_root.XamlRoot?.RasterizationScale}.");

            // The window drags from the chrome with nothing to click, and from nowhere else. Windows itself is asked, by hit
            // test, so this holds for the areas actually applied: the rail around its buttons (its mark's row included), the
            // panel header beside Hide panel, the empty strip (and the strip's head once the mark is in it, checked with the
            // sidebar on the right: with it on the left the head is empty and the first tab starts at the rail's edge). Not a
            // rail button, Hide panel, the panel's resize grip, any tab, the search box or the terminal. With the panel shut the
            // areas are recomputed and the tab that moved into the header's place takes clicks; while a dialog is up nothing drags.
            var dragChecks = new Dictionary<string, bool>();
            void Drag(string name, FrameworkElement element, double x, double y, bool expected) => dragChecks[name] = IsDragPoint(element, x, y) == expected;
            double RailGap() => (_railItems["sessions"].Button.TransformToVisual(_rail).TransformPoint(new(0, 40)).Y + _railItems["new"].Button.TransformToVisual(_rail).TransformPoint(default).Y) / 2;
            void Chrome(string when)
            {
                Drag(when + "-rail", _rail, RailWidth / 2, RailGap(), true);
                Drag(when + "-mark", _rail, RailWidth / 2, StripHeight / 2, true);
                foreach (var (id, item) in _railItems) Drag(when + "-button-" + id, item.Button, 20, 20, false);
                Drag(when + "-strip", _titleBar, _titleBar.ActualWidth / 2, StripHeight / 2, true);
                // Every tab: with the panel shut, the first one sits where the panel header's drag area was.
                foreach (var (item, index) in _tabs.TabItems.OfType<TabViewItem>().Select((item, index) => (item, index)))
                    if (_open.TryGetValue(item, out var open)) Drag(when + "-tab-" + index, open.Header, 10, TabContentHeight / 2, false);
                Drag(when + "-terminal", pane, pane.ActualWidth / 2, pane.ActualHeight / 2, false);
            }
            Chrome("open");
            Drag("open-header", _panelHide, -12, _panelHide.ActualHeight / 2, true);
            Drag("open-title", _panelTitle, 4, _panelTitle.ActualHeight / 2, true);
            Drag("open-hide", _panelHide, _panelHide.ActualWidth / 2, _panelHide.ActualHeight / 2, false);
            Drag("open-grip", _panelEdge, PaneSplitter.GrabSize / 2.0, StripHeight / 2, false);
            Drag("open-search", _search, 20, _search.ActualHeight / 2, false);
            // The areas follow the layout pass that moves things, not the call that asked for the move.
            var headerOpenAt = _stripHeader.TransformToVisual(null).TransformPoint(default).X;
            var openAreas = _dragRects;
            await SetPanelOpen(false);
            dragChecks["shut-laid-out"] = await EventuallyAsync(() => _stripHeader.TransformToVisual(null).TransformPoint(default).X < headerOpenAt - 100);
            dragChecks["shut-recomputed"] = await EventuallyAsync(() => !_dragRects.SequenceEqual(openAreas));
            // The first recomputation can land mid-slide, when the areas have changed but the panel header's are still there over
            // the first tab; the areas are waited for to settle, so a check fails only when they never do (seen October 13, 2026:
            // some runs read the areas between the two passes).
            var settleClock = System.Diagnostics.Stopwatch.StartNew();
            var firstTab = _tabs.TabItems.OfType<TabViewItem>().Select(item => _open.GetValueOrDefault(item)?.Header).FirstOrDefault(header => header is not null);
            dragChecks["shut-settled"] = firstTab is null || await EventuallyAsync(() => !IsDragPoint(firstTab, 10, TabContentHeight / 2));
            var dragSettledMs = settleClock.ElapsedMilliseconds;
            Chrome("shut");
            var dialog = Dialog("Drag areas", _theme.Caption("Verification"), "Close");
            var dialogShown = dialog.ShowAsync().AsTask();
            await WaitForAsync(() => Task.FromResult(_dialogsOpen == 1));
            Drag("dialog-rail", _rail, RailWidth / 2, RailGap(), false);
            Drag("dialog-strip", _titleBar, _titleBar.ActualWidth / 2, StripHeight / 2, false);
            dialog.Hide();
            await dialogShown;
            dragChecks["after-dialog-rail"] = await EventuallyAsync(() => _dialogsOpen == 0 && IsDragPoint(_rail, RailWidth / 2, RailGap()));
            await SetPanelOpen(true);
            var dragRegions = dragChecks.Values.All(passed => passed);
            if (!dragRegions)
                throw new InvalidOperationException($"The window's drag areas were wrong at: {string.Join(", ", dragChecks.Where(check => !check.Value).Select(check => check.Key))}. Settled after {dragSettledMs} ms. Areas: {DragAreaText(_dragRects)} scale={_root.XamlRoot?.RasterizationScale}.");

            // The sidebar on the right, set through the Settings code path and saved. The rail at the window's right edge, the panel
            // between it and the terminal area with its resize edge on its left, and the body pinned to that edge; the mark at the
            // head of the tab strip, since the close button covers the rail's top; the panel header in the rail's color with its end
            // kept clear of the minimize and maximize buttons, and the strip clear of them once the panel is shut. The window still
            // drags from the chrome and nowhere else, the caption buttons included, which Windows is asked about by hit test. Then
            // back to the left for the checks after.
            var themeBeforeRight = _theme.Definition.Id;
            SetSidebarSide(SidebarSide.Right);
            var rightChecks = new Dictionary<string, bool>();
            double LeftOf(FrameworkElement element) => element.TransformToVisual(_root).TransformPoint(default).X;
            static bool Near(double a, double b) => Math.Abs(a - b) < 1.5;
            var captionWidth = AppWindow.TitleBar.RightInset / (_root.XamlRoot?.RasterizationScale ?? 1);
            rightChecks["laid-out"] = await EventuallyAsync(() => Near(LeftOf(_rail), _root.ActualWidth - RailWidth) && Near(LeftOf(_panel) + _panel.ActualWidth, LeftOf(_rail)));
            rightChecks["panel-width"] = Near(_panel.ActualWidth, PanelLayout.DefaultWidth) && _panelOpen;
            rightChecks["terminals-left"] = Near(LeftOf(_work), 0) && Near(LeftOf(_work) + _work.ActualWidth, LeftOf(_panel));
            rightChecks["edge-left"] = Near(LeftOf(_panelEdge) + PaneSplitter.GrabSize / 2, LeftOf(_panel)) && _panelEdge.Reversed;
            rightChecks["body-pinned"] = _panelBody.Margin == new Thickness(0) && _panelBody.HorizontalAlignment == HorizontalAlignment.Left && Near(LeftOf(_panelBody), LeftOf(_panel));
            rightChecks["mark-moved"] = _railMark.Visibility == Visibility.Collapsed && _stripMark.Visibility == Visibility.Visible && Near(LeftOf(_stripMark), 0);
            rightChecks["header-clear"] = captionWidth > RailWidth && Near(_panelHeader.Padding.Right, captionWidth - RailWidth + 8) && _captionSpacer.Width == 0
                && ReferenceEquals(_panelHeader.Background, _theme.Rail) && ReferenceEquals(_panelTitle.Foreground, _theme.StripText);
            rightChecks["hide-mirrored"] = Grid.GetColumn(_panelHide) == 0 && _panelHide.Content is FontIcon { Glyph: "" } && _panelFootHide.HorizontalAlignment == HorizontalAlignment.Left && Near(LeftOf(_panelFootHide), LeftOf(_panelHide)) && _panelFootHide.Content is FontIcon { Glyph: "\uE8A0" }
                && _panelFoot.ActualHeight > 0 && _panelFoot.Margin.Bottom == 10;
            rightChecks["pill-outside"] = _railItems.Values.All(item => item.Pill.HorizontalAlignment == HorizontalAlignment.Right);
            rightChecks["saved"] = SavedPreferences(() => new AppPreferences()).Sidebar == SidebarSide.Right;
            await terminal.SendFromRendererAsync("PING:RIGHT\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:RIGHT"));
            rightChecks["terminal-answers"] = pane.IsRunning;
            rightChecks["recomputed"] = await EventuallyAsync(() => IsDragPoint(_stripMark, RailWidth / 2, _stripMark.ActualHeight / 2));
            var closeButtonHit = HitTest(_rail, RailWidth / 2, 16);
            void RightDrag(string name, FrameworkElement element, double x, double y, bool expected) => rightChecks["drag-" + name] = IsDragPoint(element, x, y) == expected;
            RightDrag("rail", _rail, RailWidth / 2, RailGap(), true);
            // The head less the workspace indicator is the drag area; the mark sits in the head with the sidebar on the right.
            RightDrag("head", _stripMark, RailWidth / 2, StripHeight / 2, true);
            RightDrag("strip", _titleBar, _titleBar.ActualWidth / 2, StripHeight / 2, true);
            RightDrag("title", _panelTitle, 4, _panelTitle.ActualHeight / 2, true);
            RightDrag("hide", _panelHide, _panelHide.ActualWidth / 2, _panelHide.ActualHeight / 2, false);
            RightDrag("grip", _panelEdge, PaneSplitter.GrabSize / 2.0, StripHeight / 2, false);
            RightDrag("close-button", _rail, RailWidth / 2, 16, false);
            RightDrag("minimize-button", _panelHeader, _panelHeader.ActualWidth - (captionWidth - RailWidth) + 12, 16, false);
            foreach (var (id, item) in _railItems) RightDrag("button-" + id, item.Button, 20, 20, false);
            RightDrag("search", _search, 20, _search.ActualHeight / 2, false);
            RightDrag("terminal", pane, pane.ActualWidth / 2, pane.ActualHeight / 2, false);
            var rightImage = await CaptureShellAsync(Path.Combine(data, "sidebar-right-native.png"), terminal);
            ApplyTheme("sic-light");
            var rightLightImage = await CaptureShellAsync(Path.Combine(data, "sidebar-right-light-native.png"), terminal);
            ApplyTheme(themeBeforeRight);
            // Shut, the strip reaches under the minimize and maximize buttons and keeps that much clear; the last tab stays out.
            await SetPanelOpen(false);
            rightChecks["shut-strip-clear"] = await EventuallyAsync(() => Near(_captionSpacer.Width, captionWidth - RailWidth)
                && _tabs.TabItems.OfType<TabViewItem>().LastOrDefault() is { } last && LeftOf(last) + last.ActualWidth <= _root.ActualWidth - captionWidth + 1);
            rightChecks["shut-recomputed"] = await EventuallyAsync(() => IsDragPoint(_titleBar, _titleBar.ActualWidth / 2, StripHeight / 2));
            RightDrag("shut-rail", _rail, RailWidth / 2, RailGap(), true);
            RightDrag("shut-close-button", _rail, RailWidth / 2, 16, false);
            var rightShutImage = await CaptureShellAsync(Path.Combine(data, "sidebar-right-shut-native.png"), terminal);
            await SetPanelOpen(true);
            SetSidebarSide(SidebarSide.Left);
            rightChecks["back-left"] = await EventuallyAsync(() => Near(LeftOf(_rail), 0) && Near(LeftOf(_panel), RailWidth)) && _railMark.Visibility == Visibility.Visible
                && _stripMark.Visibility == Visibility.Collapsed && Near(_captionSpacer.Width, captionWidth) && _panelHeader.Padding == new Thickness(16, 0, 6, 0)
                && ReferenceEquals(_panelHeader.Background, _theme.Panel) && !_panelEdge.Reversed && _panelHide.Content is FontIcon { Glyph: "" } && _panelFootHide.HorizontalAlignment == HorizontalAlignment.Right && Near(LeftOf(_panelFootHide), LeftOf(_panelHide)) && _panelFootHide.Content is FontIcon { Glyph: "\uE89F" };
            rightChecks["back-left-drags"] = await EventuallyAsync(() => IsDragPoint(_rail, RailWidth / 2, StripHeight / 2) && IsDragPoint(_panelTitle, 4, _panelTitle.ActualHeight / 2));
            // Narrowed, the tabs overflow and the footer stays whole at the strip's end, the tab list ending where it starts: with
            // room for some tabs, after a round trip through full screen, whose caption spacer comes back without the strip's width
            // changing, and with the terminal area narrower than the footer, where TabView stops sizing its list itself.
            var wideSize = AppWindow.Size;
            var keptBounds = _restoredBounds;
            var narrowScale = _root.XamlRoot?.RasterizationScale ?? 1;
            var narrowTrace = new Dictionary<string, string>();
            bool StripFits(string step)
            {
                var list = FindNamed<ListView>(_tabs, "TabListView");
                // A strip narrower than its footer has no room for tabs at all, and the list takes none.
                var fits = list is not null && LeftOf(list) + list.ActualWidth <= LeftOf(_stripFooter) + 1
                    && (Near(LeftOf(_captionSpacer) + _captionSpacer.ActualWidth, LeftOf(_tabs) + _tabs.ActualWidth) || _tabs.ActualWidth < StripFooterWidth() && list.ActualWidth == 0);
                if (!fits) narrowTrace[step] = ($"work {_work.ActualWidth:0.#} tabs {LeftOf(_tabs):0.#}+{_tabs.ActualWidth:0.#} list {(list is null ? "none" : $"{LeftOf(list):0.#}+{list.ActualWidth:0.#} max {list.MaxWidth:0.#}")} footer {LeftOf(_stripFooter):0.#}+{_stripFooter.ActualWidth:0.#} spacer {LeftOf(_captionSpacer):0.#}+{_captionSpacer.ActualWidth:0.#}");
                return fits;
            }
            foreach (var workWidth in new[] { 300, 100 })
            {
                AppWindow.ResizeClient(new((int)Math.Round((RailWidth + _panel.ActualWidth + workWidth) * narrowScale), AppWindow.ClientSize.Height));
                rightChecks[$"narrow-{workWidth}"] = await EventuallyAsync(() => _work.ActualWidth < workWidth + 2 && StripFits($"narrow-{workWidth}"));
                if (workWidth != 300) continue;
                // Full screen returns to the bounds placement tracking keeps, which the smoke turns on only at its end.
                _restoredBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
                SetFullScreen(true);
                await EventuallyAsync(() => _captionSpacer.Width == 0 && _work.ActualWidth > workWidth + 100);
                SetFullScreen(false);
                rightChecks["narrow-after-full-screen"] = await EventuallyAsync(() => !_fullScreen && _work.ActualWidth < workWidth + 2 && _captionSpacer.Width > 0 && StripFits("narrow-after-full-screen"));
            }
            _restoredBounds = keptBounds;
            AppWindow.Resize(wideSize);
            rightChecks["narrow-restored"] = await EventuallyAsync(() => Near(LeftOf(_captionSpacer) + _captionSpacer.ActualWidth, _root.ActualWidth));
            var sidebarRight = rightChecks.Values.All(passed => passed);
            if (!sidebarRight)
                throw new InvalidOperationException($"The sidebar on the right was wrong at: {string.Join(", ", rightChecks.Where(check => !check.Value).Select(check => check.Key))}. Caption {captionWidth} DIPs, close button hit {closeButtonHit}, narrowed: {string.Join("; ", narrowTrace.Select(step => step.Key + ": " + step.Value))}, areas: {DragAreaText(_dragRects)}.");

            // The bars at the bottom (docs/decisions/tab-bar-side.md), set through the Settings code path and saved. The strip and
            // the content trade rows in the TabView's template: the strip along the bottom of the terminal area, the panes ending on
            // its top edge, where the selected tab begins, its 8 px inset and border lines on its far side; every tab turned over
            // with its header turned back, and every pane's header under its terminal, where the selected tab meets it. No title
            // bar: Windows draws no buttons, the panes reach the window's top edge, and the window keeps its frame, its styles and
            // the top edge's resize border. SIC's own buttons at the strip's end send Windows' commands, and pressed for real they
            // maximize and restore, minimize, and close to the tray. The window drags from the strip, down there, and from the
            // rail; the Alt keys are mirrored (Alt+Down from the lone pane to the strip, Alt+Up left to its terminal by the sides
            // the page holds); a pane dragged onto the bottom strip lands in it, and the strip bar is drawn there. Two panes made
            // now, one over the other, each have their header under them. With the rail on the right as well, the mark stays atop
            // the rail and the panel header keeps the panel's color. Then back to the top, as before.
            _tabs.SelectedItem = shellHost.Item;
            SetTabBarSide(TabBarSide.Bottom);
            var bottomChecks = new Dictionary<string, bool>();
            double TopOf(FrameworkElement element) => element.TransformToVisual(_root).TransformPoint(default).Y;
            bool HeaderBelow(SessionPane item) => item.HeaderAtBottom && item.Header.BorderThickness == new Thickness(0, 1, 0, 0)
                && Near(TopOf(item.Header) + item.Header.ActualHeight, TopOf(item) + item.ActualHeight);
            var stripGrid = FindNamed<Grid>(_tabs, "TabContainerGrid");
            var stripFrame = stripGrid is null ? null : Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(stripGrid) as Grid;
            bottomChecks["laid-out"] = await EventuallyAsync(() => stripGrid is not null && Near(TopOf(stripGrid) + stripGrid.ActualHeight, TopOf(_tabs) + _tabs.ActualHeight)
                && Near(TopOf(_stripBackdrop), TopOf(stripGrid)) && Near(TopOf(shellHost.Content) + shellHost.Content.ActualHeight, TopOf(stripGrid)));
            bottomChecks["template-rows"] = stripFrame is { RowDefinitions.Count: 2 } && stripFrame.RowDefinitions[0].Height.IsStar && stripFrame.RowDefinitions[1].Height.IsAuto && Grid.GetRow(stripGrid!) == 1;
            var titleBar = AppWindow.TitleBar;
            bottomChecks["no-title-bar"] = await EventuallyAsync(() => titleBar.PreferredHeightOption == Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed && titleBar.RightInset == 0)
                && Near(TopOf(shellHost.Content), TopOf(_work)) && _captionSpacer.Width == 0 && CaptionButtons().Width == 0;
            // The frame stays the window's: caption, window menu, sizing border, minimize and maximize boxes, so Win+arrows, the
            // minimize and maximize animations and snapping still apply; and the top edge still sizes the window (HTTOP).
            const long FrameStyles = 0x00C00000 | 0x00080000 | 0x00040000 | 0x00020000 | 0x00010000;
            var windowStyles = (long)GetWindowLongPtr(WinRT.Interop.WindowNative.GetWindowHandle(this), -16);
            var topEdgeHit = HitTest(_work, _work.ActualWidth / 2, 1);
            bottomChecks["frame-kept"] = (windowStyles & FrameStyles) == FrameStyles && topEdgeHit == 12;
            bottomChecks["tabs-turned"] = _open.Values.Count(TabTurned) == _open.Count;
            var stripEdges = stripGrid is null ? [] : AllNamed(stripGrid, "LeftBottomBorderLine", "RightBottomBorderLine").ToArray();
            bottomChecks["edges-far"] = stripEdges.Length >= 2 && stripEdges.All(edge => edge.VerticalAlignment == VerticalAlignment.Top) && _tabs.Padding == new Thickness(0);
            // No inset beyond the tabs: the strip, its backdrop and the window buttons are as tall as a tab.
            bottomChecks["strip-trimmed"] = await EventuallyAsync(() => stripGrid is not null && Near(stripGrid.ActualHeight, BottomStripHeight) && Near(_stripBackdrop.ActualHeight, BottomStripHeight));
            bottomChecks["tab-meets-panes"] = stripGrid is not null && Near(TopOf(shellHost.Item), TopOf(stripGrid));
            bottomChecks["headers-below"] = AllPanes.All(item => item.HeaderAtBottom) && HeaderBelow(pane)
                && stripGrid is not null && Near(TopOf(pane.Header) + pane.Header.ActualHeight, TopOf(stripGrid));
            // The 6 px a closed notice leaves between the strip and the status bar is the strip's color, not a line of the base.
            bottomChecks["notice-row"] = ReferenceEquals(_noticeHost.Background, _theme.Rail);
            bottomChecks["saved"] = SavedPreferences(() => new AppPreferences()).TabBar == TabBarSide.Bottom;
            await terminal.SendFromRendererAsync("PING:BOTTOM\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:BOTTOM"));
            bottomChecks["terminal-answers"] = pane.IsRunning;
            // A lone pane: every side but the bottom is the tab's edge, and the strip is below it.
            var loneSides = new PaneNeighbors(false, false, false, true);
            var keysMirrored = shellHost.Panes.Count == 1 && pane.Neighbors == loneSides;
            try
            {
                // The page's sides leave Alt+Up to the terminal; pressed here it would reach the fixture's input line, so it is not.
                await WaitForAsync(async () => await terminal.ReadNeighborsAsync() == loneSides);
                keysMirrored &= await terminal.PressKeyAsync("ArrowDown", alt: true);
                await WaitForAsync(() => Task.FromResult(ReferenceEquals(FocusManager.GetFocusedElement(_root.XamlRoot), shellHost.Item)));
                keysMirrored &= EnterTab(shellHost.Item);
                await WaitForAsync(() => Task.FromResult(terminal.HasFocus));
            }
            catch (OperationCanceledException) { keysMirrored = false; }
            bottomChecks["keys-mirrored"] = keysMirrored && KeyboardReference.SelectMany(group => group.Shortcuts).Any(shortcut => shortcut.Keys == "Alt+Down");
            var band = StripBand;
            bottomChecks["strip-target"] = PaneDragTargetAt(pane, new Windows.Foundation.Point(8, band.Top + StripHeightNow / 2)).StripIndex is not null
                && PaneDragTargetAt(pane, new Windows.Foundation.Point(8, StripHeightNow / 2)).StripIndex is null;
            ShowStripBar(0);
            bottomChecks["strip-bar"] = Near(Canvas.GetTop(_stripBar), TopOf(_stripBackdrop) - TopOf(_stripOverlay) + 6);
            HideStripBar();
            // SIC's window buttons: at the strip's end, as wide as Windows' own, named as Windows names them, each sending the
            // command Windows' button sends.
            Button[] windowButtons = [_minimizeButton, _maximizeButton, _closeButton];
            bottomChecks["buttons"] = _windowButtons.Visibility == Visibility.Visible && windowButtons.All(button => Near(button.ActualWidth, 46) && Near(button.ActualHeight, BottomStripHeight)
                    && TopOf(button) > TopOf(_stripBackdrop) - 1.5 && TopOf(button) + button.ActualHeight < TopOf(_stripBackdrop) + _stripBackdrop.ActualHeight + 1.5)
                && Near(LeftOf(_closeButton) + _closeButton.ActualWidth, LeftOf(_work) + _work.ActualWidth) && _maximizeButton.Content is FontIcon { Glyph: MaximizeGlyph }
                && windowButtons.Select(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)).SequenceEqual(["Minimize", "Maximize", "Close"]);
            var commands = new List<nuint>();
            _windowCommandSpy = commands.Add;
            foreach (var button in windowButtons) Press(button);
            _windowCommandSpy = null;
            bottomChecks["button-commands"] = commands.SequenceEqual([MinimizeCommand, MaximizeCommand, CloseCommand]);
            // Pressed for real: maximize, then Restore Down to the size it had; minimize, which with Close and minimize at its
            // default hides the window to the tray with its terminal running, and with Close to tray, minimize to taskbar
            // minimizes it, brought back each time as the tray brings it back; close, which hides the window to the tray with its
            // terminal running, brought back the same way.
            var sizeBefore = AppWindow.Size;
            Press(_maximizeButton);
            var maximizedByButton = await EventuallyAsync(() => Maximized && _maximizeButton.Content is FontIcon { Glyph: RestoreGlyph }
                && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(_maximizeButton) == "Restore Down");
            Press(_maximizeButton);
            bottomChecks["maximize-button"] = maximizedByButton && await EventuallyAsync(() => !Maximized && AppWindow.Size.Width == sizeBefore.Width
                && AppWindow.Size.Height == sizeBefore.Height && _maximizeButton.Content is FontIcon { Glyph: MaximizeGlyph });
            Press(_minimizeButton);
            var minimizedToTray = await EventuallyAsync(() => !AppWindow.IsVisible) && pane.IsRunning;
            ShowFromTray();
            await WaitForAsync(() => Task.FromResult(AppWindow.IsVisible));
            SetCloseAndMinimize(CloseAndMinimize.CloseToTray);
            Press(_minimizeButton);
            bottomChecks["minimize-button"] = minimizedToTray
                && await EventuallyAsync(() => AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized });
            SetCloseAndMinimize(CloseAndMinimize.Tray);
            ShowFromTray();
            await WaitForAsync(() => Task.FromResult(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Restored }));
            Press(_closeButton);
            bottomChecks["close-button"] = await EventuallyAsync(() => !AppWindow.IsVisible) && pane.IsRunning;
            ShowFromTray();
            await WaitForAsync(() => Task.FromResult(AppWindow.IsVisible));
            bottomChecks["recomputed"] = await EventuallyAsync(() => IsDragPoint(_titleBar, _titleBar.ActualWidth / 2, StripHeight / 2));
            void BottomDrag(string name, FrameworkElement element, double x, double y, bool expected) => bottomChecks["drag-" + name] = IsDragPoint(element, x, y) == expected;
            BottomDrag("rail", _rail, RailWidth / 2, RailGap(), true);
            BottomDrag("mark", _railMark, RailWidth / 2, StripHeight / 2, true);
            BottomDrag("title", _panelTitle, 4, _panelTitle.ActualHeight / 2, true);
            BottomDrag("strip-end", _titleBar, _titleBar.ActualWidth / 2, StripHeight / 2, true);
            foreach (var button in windowButtons) BottomDrag("button-" + Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button), button, 23, button.ActualHeight / 2, false);
            BottomDrag("tab", shellHost.Header, 10, TabContentHeight / 2, false);
            BottomDrag("header", pane.Header, pane.Header.ActualWidth / 3, pane.Header.ActualHeight / 2, false);
            BottomDrag("terminal", pane, pane.ActualWidth / 2, pane.ActualHeight / 2, false);
            // Where Windows' close button was, the terminal's own corner, which keeps its presses.
            var cornerHit = HitTest(pane, pane.ActualWidth - 20, 12);
            bottomChecks["corner-client"] = cornerHit == 1;
            var tabsBottomImage = await CaptureShellAsync(Path.Combine(data, "tabs-bottom-native.png"), terminal);
            // And as Windows composes the window, the live terminal included.
            var tabsBottomWindowImage = await CaptureWindowAsync(Path.Combine(data, "tabs-bottom-window.png")) is not null ? Path.Combine(data, "tabs-bottom-window.png") : null;
            NotifyBriefly("Tabs", "The tabs are at the bottom.");
            await Task.Delay(500);
            var tabsBottomNoticeImage = await CaptureShellAsync(Path.Combine(data, "tabs-bottom-notice-native.png"), terminal);
            _notice.IsOpen = false;
            var themeBeforeBottom = _theme.Definition.Id;
            ApplyTheme("sic-light");
            var tabsBottomLightImage = await CaptureShellAsync(Path.Combine(data, "tabs-bottom-light-native.png"), terminal);
            ApplyTheme(themeBeforeBottom);
            // Two panes made now, one over the other: each header under its own terminal, the upper one's on the divider.
            // Placeholders, so nothing starts.
            var stackedPanes = Enumerable.Range(1, 2).Select(index => new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · stacked " + index, data, "")).ToList();
            var stackedHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "PowerShell · stacked", stackedPanes,
                Layout: PaneLayout.SplitNode(PaneSplit.Rows, 0.5, PaneLayout.Leaf(stackedPanes[0].Id), PaneLayout.Leaf(stackedPanes[1].Id))));
            bottomChecks["stacked-headers"] = await EventuallyAsync(() => stackedHost.Panes.Count == 2 && stackedHost.Panes.All(item => item.ActualHeight > 0 && HeaderBelow(item))
                && TopOf(stackedHost.Panes[0]) < TopOf(stackedHost.Panes[1]) && stackedHost.Item.ActualWidth > 0 && TabTurned(stackedHost));
            // A tab just added plays the strip's entrance transition, and a capture taken during it leaves the tab out (laid out,
            // turned and opaque all the same, as a trace of it showed on October 15, 2026).
            await Task.Delay(1500);
            var tabsBottomSplitImage = Path.Combine(data, "tabs-bottom-split-native.png");
            await CaptureAsync(_root, tabsBottomSplitImage);
            await CloseTabAsync(stackedHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            // With the rail on the right, Windows draws no close button over the rail's top: the mark stays there, and the panel
            // header keeps the panel's color and needs no room at its end.
            SetSidebarSide(SidebarSide.Right);
            bottomChecks["right-mark"] = await EventuallyAsync(() => Near(LeftOf(_rail), _root.ActualWidth - RailWidth)) && _railMark.Visibility == Visibility.Visible
                && _stripMark.Visibility == Visibility.Collapsed && _captionSpacer.Width == 0 && ReferenceEquals(_panelHeader.Background, _theme.Panel)
                && _panelHeader.Padding == new Thickness(6, 0, 8, 0) && Near(LeftOf(_closeButton) + _closeButton.ActualWidth, LeftOf(_work) + _work.ActualWidth);
            bottomChecks["right-mark-drags"] = await EventuallyAsync(() => IsDragPoint(_railMark, RailWidth / 2, StripHeight / 2));
            var tabsBottomRightImage = await CaptureShellAsync(Path.Combine(data, "tabs-bottom-right-native.png"), terminal);
            SetSidebarSide(SidebarSide.Left);
            SetTabBarSide(TabBarSide.Top);
            bottomChecks["back-top"] = await EventuallyAsync(() => stripGrid is not null && Near(TopOf(stripGrid), TopOf(_tabs)) && Near(TopOf(shellHost.Content), TopOf(stripGrid) + stripGrid.ActualHeight) && Near(_stripBackdrop.ActualHeight, StripHeight))
                && Grid.GetRow(stripGrid!) == 0 && stripFrame!.RowDefinitions[0].Height.IsAuto && !_open.Values.Any(TabTurned)
                && _tabs.Margin == new Thickness(0) && _tabs.Padding == new Thickness(0, StripPadding, 0, 0)
                && stripEdges.All(edge => edge.VerticalAlignment == VerticalAlignment.Bottom) && pane.Neighbors == new PaneNeighbors(false, false, true, false)
                && _railMark.Visibility == Visibility.Visible && _windowButtons.Visibility == Visibility.Collapsed && _noticeHost.Background is null
                && !pane.HeaderAtBottom && Near(TopOf(pane.Header), TopOf(pane)) && pane.Header.BorderThickness == new Thickness(0, 0, 0, 1);
            // Windows' buttons are back over the strip's end, which keeps clear of them again.
            bottomChecks["back-top-caption"] = await EventuallyAsync(() => titleBar.PreferredHeightOption == Microsoft.UI.Windowing.TitleBarHeightOption.Standard
                && Near(titleBar.RightInset / (_root.XamlRoot?.RasterizationScale ?? 1), captionWidth) && Near(_captionSpacer.Width, captionWidth));
            bottomChecks["back-top-drags"] = await EventuallyAsync(() => IsDragPoint(_titleBar, _titleBar.ActualWidth / 2, StripHeight / 2) && IsDragPoint(_rail, RailWidth / 2, StripHeight / 2));
            var tabBarBottom = bottomChecks.Values.All(passed => passed);
            if (!tabBarBottom)
                throw new InvalidOperationException($"The bars at the bottom were wrong at: {string.Join(", ", bottomChecks.Where(check => !check.Value).Select(check => check.Key))}. "
                    + $"Caption inset {titleBar.RightInset} px, height option {titleBar.PreferredHeightOption}, styles 0x{windowStyles:X}, top edge hit {topEdgeHit}, corner hit {cornerHit}, areas: {DragAreaText(_dragRects)}.");

            // Full screen (docs/decisions/full-screen.md): F11 from inside a live terminal, through the page's chord table. The
            // window fills its display through Windows' full-screen presenter, with no caption buttons and no drag areas; the
            // strip, the panes and the status bar stay; the rail and the panel leave their edge, and the terminal area takes the
            // window's width across the three columns. The pointer at the window's left edge, as the watch looks at it (by a
            // point: the smoke never takes the mouse), brings them out over the terminals, which keep their width, and the pointer
            // in the terminal sends them away. A panel opened from the keyboard holds them out, the pointer in the terminal
            // notwithstanding, until a pane has the keyboard. The strip's maximize button reads Leave full screen meanwhile.
            // Then F11 again: the window at its size, the sidebar in its column, the drag areas back; and from a maximized
            // window, full screen and back leaves it maximized.
            var fullScreenChecks = new Dictionary<string, bool>();
            var sizeBeforeFull = AppWindow.Size;
            var workWidthBefore = _work.ActualWidth;
            var f11Taken = await terminal.PressKeyAsync("F11");
            var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).OuterBounds;
            fullScreenChecks["entered"] = f11Taken && await EventuallyAsync(() => _fullScreen && AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen
                && AppWindow.Size.Width == display.Width && AppWindow.Size.Height == display.Height);
            fullScreenChecks["sidebar-away"] = await EventuallyAsync(() => !_sidebarOut && _rail.Visibility == Visibility.Collapsed && _panel.Visibility == Visibility.Collapsed
                && Grid.GetColumnSpan(_work) == 3 && Near(_work.ActualWidth, _root.ActualWidth) && Near(LeftOf(_work), 0));
            fullScreenChecks["bars-kept"] = stripGrid is not null && stripGrid.ActualHeight > 0 && Near(TopOf(stripGrid), 0) && Near(TopOf(_work) + _work.ActualHeight + 26, _root.ActualHeight)
                && _captionSpacer.Width == 0;
            fullScreenChecks["no-drag"] = await EventuallyAsync(() => _dragRects.Length == 1 && _dragRects[0] == default) && HitTest(pane, pane.ActualWidth / 2, pane.ActualHeight / 2) == 1;
            // One button at the strip's end, with the bars at the top too: Leave full screen, with the restore glyph, in the
            // window's top-right corner, and neither minimize nor close beside it.
            fullScreenChecks["button"] = await EventuallyAsync(() => _windowButtons.Visibility == Visibility.Visible && _maximizeButton.Visibility == Visibility.Visible
                    && _minimizeButton.Visibility == Visibility.Collapsed && _closeButton.Visibility == Visibility.Collapsed
                    && Near(LeftOf(_maximizeButton) + _maximizeButton.ActualWidth, _root.ActualWidth) && Near(TopOf(_maximizeButton), 0) && Near(_maximizeButton.ActualHeight, StripHeight))
                && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(_maximizeButton) == "Leave full screen" && _maximizeButton.Content is FontIcon { Glyph: RestoreGlyph };
            NativePoint ScreenPoint(FrameworkElement element, double x, double y)
            {
                var screen = _root.XamlRoot!.CoordinateConverter.ConvertLocalToScreen(element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(x, y)));
                return new NativePoint { X = screen.X, Y = screen.Y };
            }
            var fullWorkWidth = _work.ActualWidth;
            TendSidebar(ScreenPoint(_work, 1, _work.ActualHeight / 2));
            fullScreenChecks["edge-reveals"] = await EventuallyAsync(() => _sidebarOut && _rail.Visibility == Visibility.Visible && _panel.Visibility == Visibility.Visible && Near(_rail.ActualWidth, RailWidth)
                && Near(LeftOf(_rail), 0) && Near(LeftOf(_panel), RailWidth) && Near(_work.ActualWidth, fullWorkWidth) && Near(LeftOf(_work), 0));
            // The base's backing under them both, since the rail is translucent: the window's left edge to the panel's far edge, and the body's height.
            fullScreenChecks["backing"] = await EventuallyAsync(() => _sidebarBacking.Visibility == Visibility.Visible && ReferenceEquals(_sidebarBacking.Background, _theme.Base)
                && Near(LeftOf(_sidebarBacking), 0) && Near(_sidebarBacking.ActualWidth, RailWidth + _panel.ActualWidth) && Near(_sidebarBacking.ActualHeight, _rail.ActualHeight)
                && Canvas.GetZIndex(_sidebarBacking) > Canvas.GetZIndex(_work) && Canvas.GetZIndex(_rail) > Canvas.GetZIndex(_sidebarBacking));
            await Task.Delay(300);
            var fullScreenImage = Path.Combine(data, "full-screen-native.png");
            await CaptureAsync(_root, fullScreenImage);
            var fullScreenWindowImage = await CaptureWindowAsync(Path.Combine(data, "full-screen-window.png")) is not null ? Path.Combine(data, "full-screen-window.png") : null;
            TendSidebar(ScreenPoint(pane, pane.ActualWidth / 2, pane.ActualHeight / 2));
            fullScreenChecks["leaving-hides"] = await EventuallyAsync(() => !_sidebarOut && _rail.Visibility == Visibility.Collapsed && _panel.Visibility == Visibility.Collapsed && _sidebarBacking.Visibility == Visibility.Collapsed);
            await HandleShortcutAsync("search");
            var heldOut = await EventuallyAsync(() => _sidebarOut && _sidebarHeld && _panel.Visibility == Visibility.Visible && FocusInSidebar(_root.XamlRoot!));
            TendSidebar(ScreenPoint(pane, pane.ActualWidth / 2, pane.ActualHeight / 2));
            var stillOut = _sidebarOut && _panel.Visibility == Visibility.Visible;
            pane.FocusInput();
            await WaitForAsync(() => Task.FromResult(terminal.HasFocus));
            TendSidebar(ScreenPoint(pane, pane.ActualWidth / 2, pane.ActualHeight / 2));
            fullScreenChecks["keyboard-holds"] = heldOut && stillOut && await EventuallyAsync(() => !_sidebarOut && _panelOpen);
            // The rail alone (the panel closed) slides in from the edge when Menu Animation is on, seen under way off its edge
            // the moment the pointer is there and then at rest in place with its backing, and slides back to it, collapsed once
            // it has. Then the panel is open and the sidebar away again, as the checks below expect.
            var motionBeforeRail = _preferences.Motion;
            SetMotion("on");
            await SetPanelOpen(false);
            var railSoloAway = await EventuallyAsync(() => !_sidebarOut && !_panelOpen && _rail.Visibility == Visibility.Collapsed && _sidebarBacking.Visibility == Visibility.Collapsed);
            TendSidebar(ScreenPoint(_work, 1, _work.ActualHeight / 2));
            var railSliding = _sidebarOut && _rail.Visibility == Visibility.Visible && LeftOf(_rail) < -1;
            fullScreenChecks["rail-slides-out"] = railSoloAway && railSliding && await EventuallyAsync(() => Near(LeftOf(_rail), 0) && Near(LeftOf(_sidebarBacking), 0)
                && Near(_sidebarBacking.ActualWidth, RailWidth) && _panel.Visibility == Visibility.Collapsed && _sidebarBacking.Visibility == Visibility.Visible);
            TendSidebar(ScreenPoint(pane, pane.ActualWidth / 2, pane.ActualHeight / 2));
            var railLeaving = !_sidebarOut && _rail.Visibility == Visibility.Visible;
            fullScreenChecks["rail-slides-away"] = railLeaving && await EventuallyAsync(() => _rail.Visibility == Visibility.Collapsed && _sidebarBacking.Visibility == Visibility.Collapsed);
            SetMotion(motionBeforeRail);
            await SetPanelOpen(true);
            pane.FocusInput();
            await WaitForAsync(() => Task.FromResult(terminal.HasFocus));
            TendSidebar(ScreenPoint(pane, pane.ActualWidth / 2, pane.ActualHeight / 2));
            fullScreenChecks["rail-restored"] = await EventuallyAsync(() => !_sidebarOut && _panelOpen && _rail.Visibility == Visibility.Collapsed);
            // The button leaves; with the bars at the top no button is left, and with them at the bottom all three come back.
            Press(_maximizeButton);
            fullScreenChecks["left"] = await EventuallyAsync(() => !_fullScreen && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Restored }
                    && AppWindow.Size.Width == sizeBeforeFull.Width && AppWindow.Size.Height == sizeBeforeFull.Height)
                && await EventuallyAsync(() => _sidebarOut && _rail.Visibility == Visibility.Visible && _panel.Visibility == Visibility.Visible && _sidebarBacking.Visibility == Visibility.Collapsed && Grid.GetColumnSpan(_work) == 1
                    && Near(LeftOf(_work), RailWidth + _panel.ActualWidth) && Near(_work.ActualWidth, workWidthBefore))
                && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(_maximizeButton) == "Maximize" && _windowButtons.Visibility == Visibility.Collapsed
                && _minimizeButton.Visibility == Visibility.Visible && _closeButton.Visibility == Visibility.Visible;
            fullScreenChecks["drags-back"] = await EventuallyAsync(() => IsDragPoint(_rail, RailWidth / 2, StripHeight / 2) && IsDragPoint(_titleBar, _titleBar.ActualWidth / 2, StripHeight / 2));
            (AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.Maximize();
            var maximizedFirst = await EventuallyAsync(() => Maximized);
            SetFullScreen(true);
            var fullFromMaximized = await EventuallyAsync(() => _fullScreen && AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            SetFullScreen(false);
            fullScreenChecks["from-maximized"] = maximizedFirst && fullFromMaximized && await EventuallyAsync(() => !_fullScreen && Maximized);
            (AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.Restore();
            fullScreenChecks["restored"] = await EventuallyAsync(() => !Maximized && AppWindow.Size.Width == sizeBeforeFull.Width && AppWindow.Size.Height == sizeBeforeFull.Height);
            var fullScreen = fullScreenChecks.Values.All(passed => passed);
            if (!fullScreen)
                throw new InvalidOperationException($"Full screen was wrong at: {string.Join(", ", fullScreenChecks.Where(check => !check.Value).Select(check => check.Key))}. "
                    + $"F11 taken {f11Taken}, presenter {AppWindow.Presenter.Kind}, size {AppWindow.Size.Width}x{AppWindow.Size.Height}, display {display.Width}x{display.Height}, sidebar out {_sidebarOut}, areas: {DragAreaText(_dragRects)}.");

            // Autohide sidebar, in the window: with the panel open nothing changes; the panel shut puts the rail away and the
            // terminals across its column; the pointer at the edge brings it out over them, and leaving sends it away; a panel
            // opening docks the sidebar in its column; turned off, the rail is back in its column, and the setting is saved.
            var autoHideChecks = new Dictionary<string, bool>();
            var panelWasOpen = _panelOpen;
            await SetPanelOpen(true);
            SetSidebarAutoHide(true);
            autoHideChecks["open-docked"] = _sidebarOut && _rail.Visibility == Visibility.Visible && Grid.GetColumnSpan(_work) == 1 && SavedPreferences(() => new AppPreferences()).SidebarAutoHide;
            await SetPanelOpen(false);
            autoHideChecks["shut-away"] = await EventuallyAsync(() => !_sidebarOut && _rail.Visibility == Visibility.Collapsed && Grid.GetColumnSpan(_work) == 3
                && Near(LeftOf(_work), 0) && Near(_work.ActualWidth, _root.ActualWidth));
            TendSidebar(ScreenPoint(_work, 1, _work.ActualHeight / 2));
            autoHideChecks["edge-reveals"] = await EventuallyAsync(() => _sidebarOut && _rail.Visibility == Visibility.Visible && Near(LeftOf(_rail), 0)
                && _sidebarBacking.Visibility == Visibility.Visible && Near(_work.ActualWidth, _root.ActualWidth) && Near(TopOf(_rail), TabsBottom ? 0 : StripHeight) && Near(TopOf(_sidebarBacking), TopOf(_rail)));
            TendSidebar(ScreenPoint(pane, pane.ActualWidth / 2, pane.ActualHeight / 2));
            autoHideChecks["leaving-hides"] = await EventuallyAsync(() => !_sidebarOut && _rail.Visibility == Visibility.Collapsed && _sidebarBacking.Visibility == Visibility.Collapsed);
            await SetPanelOpen(true);
            autoHideChecks["panel-docks"] = await EventuallyAsync(() => _sidebarOut && _rail.Visibility == Visibility.Visible && _sidebarBacking.Visibility == Visibility.Collapsed
                && Grid.GetColumnSpan(_work) == 1 && Near(LeftOf(_work), RailWidth + _panel.ActualWidth));
            await SetPanelOpen(false);
            var awayAgain = await EventuallyAsync(() => !_sidebarOut && _rail.Visibility == Visibility.Collapsed);
            SetSidebarAutoHide(false);
            autoHideChecks["off-docks"] = awayAgain && await EventuallyAsync(() => _sidebarOut && _rail.Visibility == Visibility.Visible && Grid.GetColumnSpan(_work) == 1 && Near(LeftOf(_work), RailWidth))
                && !SavedPreferences(() => new AppPreferences()).SidebarAutoHide;
            await SetPanelOpen(panelWasOpen);
            if (autoHideChecks.Values.Any(passed => !passed))
                throw new InvalidOperationException($"Autohide sidebar was wrong at: {string.Join(", ", autoHideChecks.Where(check => !check.Value).Select(check => check.Key))}; sidebar out {_sidebarOut}, rail {_rail.Visibility}, work at {LeftOf(_work)} spanning {Grid.GetColumnSpan(_work)}.");

            // Chrome: frosted, the window has the acrylic backdrop, a clear root, a solid base under the terminal
            // area and a part-clear rail; off, everything is as the theme has it. Saved either way.
            var frostChecks = new Dictionary<string, bool>();
            var frostRailBefore = _theme.Rail.Color;
            SetChrome(ChromeStyle.Frosted);
            // The backdrop joins the window over the next frames.
            await Task.Delay(250);
            frostChecks["on"] = _backdrop.Style == ChromeStyle.Frosted && !_backdrop.ClearDrawn && _root.Background is null && ReferenceEquals(_workBacking.Background, _theme.Base)
                && _theme.Rail.Color.A is > 0 and < 255 && _theme.Panel.Color.A is > 0 and < 255 && _theme.Base.Color.A == 255
                && SavedPreferences(() => new AppPreferences()).Chrome == ChromeStyle.Frosted;
            // A theme change while frosted re-tints the blur and keeps the chrome part-clear.
            var frostTheme = _theme.Definition.Id;
            ApplyTheme(ThemeCatalog.Toggle(_theme.Definition).Id);
            await Task.Delay(250);
            frostChecks["theme"] = _backdrop.Tint == _theme.Base.Color && _theme.Rail.Color.A is > 0 and < 255 && _root.Background is null;
            ApplyTheme(frostTheme);
            // Clear: no backdrop, Windows composing the window with its alpha, and the same part-clear chrome over a solid terminal area.
            SetChrome(ChromeStyle.Clear);
            await Task.Delay(250);
            frostChecks["clear"] = _backdrop.Style == ChromeStyle.Clear && _backdrop.ClearDrawn && _root.Background is null && ReferenceEquals(_workBacking.Background, _theme.Base)
                && _theme.Rail.Color.A is > 0 and < 255 && SavedPreferences(() => new AppPreferences()).Chrome == ChromeStyle.Clear;
            ApplyTheme(ThemeCatalog.Toggle(_theme.Definition).Id);
            await Task.Delay(250);
            frostChecks["clearTheme"] = _backdrop.ClearDrawn && _theme.Rail.Color.A is > 0 and < 255 && _root.Background is null;
            ApplyTheme(frostTheme);
            SetChrome(ChromeStyle.Solid);
            await Task.Delay(250);
            frostChecks["off"] = _backdrop.Style == ChromeStyle.Solid && !_backdrop.ClearDrawn && _workBacking.Background is null && _theme.Rail.Color == frostRailBefore
                && (_root.Background is null) == _theme.Definition.Mica && SavedPreferences(() => new AppPreferences()).Chrome == ChromeStyle.Solid;
            if (frostChecks.Values.Any(passed => !passed))
                throw new InvalidOperationException($"The chrome was wrong at: {string.Join(", ", frostChecks.Where(check => !check.Value).Select(check => check.Key))}; rail {_theme.Rail.Color}, style {_backdrop.Style}.");

            // Window opacity: below 100 % the window is layered and blended at that share, through full screen and back;
            // at 100 % it is not layered. Saved either way, and a value out of range is brought into it.
            var opacityChecks = new Dictionary<string, bool>();
            opacityChecks["solid"] = ReadWindowOpacity() == (false, (byte)255) && _preferences.WindowOpacity == 100;
            SetWindowOpacity(80);
            opacityChecks["on"] = ReadWindowOpacity() == (true, (byte)204) && SavedPreferences(() => new AppPreferences()).WindowOpacity == 80 && (_windowOpacityValue is null || _windowOpacityValue.Text == "80%");
            SetWindowOpacity(10);
            opacityChecks["clamped"] = _preferences.WindowOpacity == WindowOpacityMin && ReadWindowOpacity().Layered;
            SetWindowOpacity(100);
            await Task.Delay(100);
            opacityChecks["off"] = ReadWindowOpacity() == (false, (byte)255) && SavedPreferences(() => new AppPreferences()).WindowOpacity == 100;
            if (opacityChecks.Values.Any(passed => !passed))
                throw new InvalidOperationException($"Window opacity was wrong at: {string.Join(", ", opacityChecks.Where(check => !check.Value).Select(check => check.Key))}; {ReadWindowOpacity()}, saved {_preferences.WindowOpacity}.");

            // Every theme reaches the chrome and leaves the terminal's own palette alone.
            // Themes as files (docs/decisions/theme-files.md): a minimal file of the user's own in the data folder's themes folder
            // is listed after Reload and applied, its chrome reaching the window and the choice saved; a broken file is named
            // under the Theme buttons and not listed; with both gone, the list is the built-in six again and the theme falls
            // back to the default. Then every theme in the catalog, which is the six again, is applied below.
            var themesFolder = ThemesFolder;
            Directory.CreateDirectory(themesFolder);
            File.WriteAllText(Path.Combine(themesFolder, "ocean.json"), """{ "id": "ocean-dark", "name": "Ocean dark", "light": false, "chrome": { "base": "#101820", "text": "#E8F0F8", "accent": "#4CC2FF" } }""");
            File.WriteAllText(Path.Combine(themesFolder, "broken.json"), "{ \"id\": \"broken\"");
            ReloadThemes(notify: false);
            ApplyTheme("ocean-dark");
            var userThemeApplied = _themeItems.ContainsKey("ocean-dark") && !_themeItems.ContainsKey("broken") && _preferences.Theme == "ocean-dark" && _theme.Definition.Id == "ocean-dark"
                && _theme.Base.Color == Theme.Parse("101820") && _theme.Accent.Color == Theme.Parse("4CC2FF") && _theme.Definition.Family == "Ocean dark"
                && ThemeCatalog.Problems.Count == 1 && ThemeCatalog.Problems[0].File == "broken.json"
                && _themeProblems.Visibility == Visibility.Visible && _themeProblems.Text.StartsWith("broken.json: ", StringComparison.Ordinal)
                && SavedPreferences(() => new AppPreferences()).Theme == "ocean-dark";
            File.Delete(Path.Combine(themesFolder, "ocean.json"));
            File.Delete(Path.Combine(themesFolder, "broken.json"));
            ReloadThemes(notify: false);
            var userThemeGone = ThemeCatalog.All.Count == ThemeCatalog.BuiltIn.Count && !_themeItems.ContainsKey("ocean-dark") && _theme.Definition.Id == ThemeCatalog.Default.Id
                && _preferences.Theme == ThemeCatalog.Default.Id && _themeProblems.Visibility == Visibility.Collapsed;
            var userThemes = userThemeApplied && userThemeGone;
            if (!userThemes) throw new InvalidOperationException($"A theme file was not listed, applied and reported, or did not go when its file went: applied={userThemeApplied} gone={userThemeGone} theme={_theme.Definition.Id} problems={ThemeCatalog.Problems.Count}.");
            var themesApplied = true;
            var railAccentReadable = true;
            var terminalPaletteFixed = true;
            var themeImages = new Dictionary<string, object>();
            foreach (var theme in ThemeCatalog.All)
            {
                ApplyTheme(theme.Id);
                var appearance = await terminal.ReadAppearanceAsync();
                var applied = _root.RequestedTheme == (theme.Light ? ElementTheme.Light : ElementTheme.Dark) && _preferences.Theme == theme.Id && pane.IsRunning
                    && _themePicker.SelectedItem is ComboBoxItem { Tag: string pickedTheme } && pickedTheme == theme.Id;
                var fixedPalette = appearance.Background == FixedTerminalBackground && appearance.Foreground == "#" + ThemeCatalog.Terminal.Foreground.ToLowerInvariant()
                    && terminal.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var canvas } && canvas == Theme.Parse(ThemeCatalog.Terminal.Background);
                // The mark follows the theme too: the rail's draws the set for the rail's tone (the white >_ on a dark rail, SIC
                // light's included, and the near-black one on a light rail) and the welcome page's the set for the page's.
                var railMarkSet = SicMark.LightArt(_rail.Children.OfType<Border>().FirstOrDefault()?.Child) == SicMark.IsLightSurface(_theme.StripSurface)
                    && _welcome.Children[0] is StackPanel welcomeStack && SicMark.LightArt(welcomeStack.Children[0]) == SicMark.IsLightSurface(_theme.Base.Color);
                if (!railMarkSet) _options.Trace("mark-set-mismatch " + theme.Id);
                applied &= railMarkSet;
                if (!applied || !fixedPalette) _options.Trace("theme-mismatch " + theme.Id + " renderer=" + appearance.Background);
                // The accent the lit Resume all glyph takes stands out from the rail: the family's darker accent on a light
                // rail, its lighter one on a dark rail.
                var railAccent = Theme.Contrast(_theme.RailAccent.Color, _theme.StripSurface);
                if (railAccent < 3) _options.Trace($"rail-accent-faint {theme.Id} {Theme.Hex(_theme.RailAccent.Color)} {railAccent:0.00}:1");
                railAccentReadable &= railAccent >= 3;
                themesApplied &= applied;
                terminalPaletteFixed &= fixedPalette;
                themeImages[theme.Id] = await CaptureShellAsync(Path.Combine(data, "theme-" + theme.Id + ".png"), terminal);
            }

            // The tab strip in every theme, panel closed so every tab fits: the selected tab is colored, one other tab is colored and one is plain.
            var plainPane = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · plain", data, "");
            var plainHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), plainPane.Title, [plainPane]), select: false);
            var stripColumns = (await terminal.GetDimensionsAsync()).Columns;
            await SetPanelOpen(false);
            var tabStripLegible = true;
            var activeTabJoinsPane = _tabs.TabStripFooter is Grid footer && !footer.Children.OfType<Button>().Any();
            var activeTabColored = true;
            var tabStripImages = new Dictionary<string, object>();
            foreach (var theme in ThemeCatalog.All)
            {
                ApplyTheme(theme.Id);
                // The drag look's fill is read from the tab's template, which a tab added just before this loop may not have
                // until the next layout pass: on October 9, 2026 two runs in five failed on the first theme. Give it that pass.
                var readable = _open.Values.All(TabInkReadable);
                for (var tries = 0; tries < 40 && !readable; tries++) { await Task.Delay(50); readable = _open.Values.All(TabInkReadable); }
                if (!readable) _options.Trace("tab-ink-unreadable " + theme.Id);
                tabStripLegible &= readable;
                // No band of the base color under the tabs, and the active tab filled exactly like the pane header it meets.
                var joined = FindNamed<Grid>(_tabs, "TabContainerGrid") is { ActualHeight: > 0 } strip && Math.Abs(_stripBackdrop.Height - strip.ActualHeight) < 0.5
                    && SelectedHost is { } active && active.Panes.All(item => ReferenceEquals(item.HeaderFill, active.Paint.Selected))
                    && (Theme.Contrast(_theme.SelectedTab.Color, _theme.StripSurface) >= Theme.ActiveTabLift - 0.01 || theme.Light);
                if (!joined) _options.Trace("active-tab-seam " + theme.Id + " backdrop=" + _stripBackdrop.Height);
                activeTabJoinsPane &= joined;
                // The selected tab (pink) carries its color, the inactive blue tab only its stripe, the plain tab none.
                var colorFollows = _open.Values.All(TabColorFollowsSelection);
                if (!colorFollows) _options.Trace("tab-color-selection " + theme.Id);
                activeTabColored &= colorFollows;
                tabStripImages[theme.Id] = await CaptureShellAsync(Path.Combine(data, "tabs-" + theme.Id + ".png"), terminal);
            }
            // Since October 15, 2026 a tab is TabHeight tall with no flared feet (TrimTabFeet clips the template's root to the
            // tab's own bounds), its row of tile and title sits clear of the stripe along its top edge, the strip is exactly as
            // tall as Windows' caption buttons, and the first tab, the selected one here, starts its fill where the strip's head ends, the
            // head starting at the rail's edge and holding the workspace indicator alone with the sidebar on the left.
            var headTab = _tabs.TabItems.OfType<TabViewItem>().First();
            var headStrip = FindNamed<Grid>(_tabs, "TabContainerGrid");
            var captionHeight = AppWindow.TitleBar.Height / (_root.XamlRoot?.RasterizationScale ?? 1);
            var tabsTrimmed = await EventuallyAsync(() => _open.Keys.All(item => Math.Abs(item.ActualHeight - TabHeight) < 0.5
                    && FindNamed<Grid>(item, "LayoutRoot") is { Clip: Microsoft.UI.Xaml.Media.RectangleGeometry clip } root && Math.Abs(clip.Rect.Width - root.ActualWidth) < 0.5 && Math.Abs(clip.Rect.Height - root.ActualHeight) < 0.5
                    && _open[item].Header.TransformToVisual(item).TransformPoint(default).Y > 3.5)
                && headStrip is not null && Math.Abs(headStrip.ActualHeight - StripHeight) < 0.5 && Math.Abs(captionHeight - StripHeight) < 0.5
                && ReferenceEquals(_tabs.SelectedItem, headTab) && Near(LeftOf(_stripHeader), LeftOf(_work)) && Near(LeftOf(headTab), LeftOf(_stripHeader) + _stripHeader.ActualWidth));
            if (!tabsTrimmed)
            {
                string At(string name) => FindNamed<FrameworkElement>(_tabs, name) is { } part ? $"{name}={LeftOf(part)}+{part.ActualWidth}" : name + "=none";
                _options.Trace($"tab-geometry tab={headTab.ActualHeight} strip={headStrip?.ActualHeight} caption={captionHeight} work={LeftOf(_work)} head={LeftOf(_stripHeader)}+{_stripHeader.ActualWidth}"
                    + $" {At("TabListView")} {At("ScrollViewer")} {At("TabsItemsPresenter")} first={LeftOf(headTab)} {(FindNamed<Grid>(headTab, "TabContainer") is { } container ? "container=" + LeftOf(container) : "")}");
            }
            // A reorder's drag look ends when the reorder lands, though the template waits for the tab's own pointer release
            // (WatchTabDrags): the plain tab is put in the look and moved to the front as a reorder moves it.
            VisualStateManager.GoToState(plainHost.Item, "DragDropVisualVisible", false);
            var dragLookShown = InDragLook(plainHost.Item);
            _tabs.TabItems.Remove(plainHost.Item);
            _tabs.TabItems.Insert(0, plainHost.Item);
            for (var tries = 0; tries < 60 && InDragLook(plainHost.Item); tries++) await Task.Delay(50);
            if (!dragLookShown || InDragLook(plainHost.Item)) throw new InvalidOperationException($"A reordered tab kept its drag look: shown={dragLookShown}.");
            await SetPanelOpen(true);
            await CloseTabAsync(plainHost.Item);
            await WaitForAsync(async () => (await terminal.GetDimensionsAsync()).Columns == stripColumns);
            if (!tabStripLegible) throw new InvalidOperationException("Tab titles or tab strip ink fell below 4.5:1 contrast in a theme.");
            if (!activeTabJoinsPane) throw new InvalidOperationException("The strip still has buttons, a band of the base color shows under the tabs, or the active tab does not match its pane header in a theme.");
            if (!activeTabColored) throw new InvalidOperationException("A colored tab did not carry its color only while selected, or an uncolored tab was tinted, in a theme.");
            if (!tabsTrimmed) throw new InvalidOperationException($"A tab is not {TabHeight} px with its feet cut off, the strip is not the caption buttons' {StripHeight} px, or the first tab does not start at the strip's head (see the tab-geometry trace).");
            ApplyTheme("fluent-light");
            var lightAppearance = await terminal.ReadAppearanceAsync();
            var lightThemeApplied = _root.RequestedTheme == ElementTheme.Light && lightAppearance.Background == FixedTerminalBackground;
            var lightShellImage = await CaptureShellAsync(Path.Combine(data, "light-native.png"), terminal);
            var lightTerminalImage = Path.Combine(data, "light-terminal.png");
            await terminal.CaptureAsync(lightTerminalImage);
            ApplyTheme("fluent-dark");
            var darkAppearance = await terminal.ReadAppearanceAsync();
            var darkThemeApplied = _root.RequestedTheme == ElementTheme.Dark && darkAppearance.Background == FixedTerminalBackground;
            if (!themesApplied || !lightThemeApplied || !darkThemeApplied) throw new InvalidOperationException("A theme did not reach the chrome.");
            if (!terminalPaletteFixed) throw new InvalidOperationException("A theme change recolored the live terminal, which keeps its own palette.");
            await terminal.SendFromRendererAsync("PING:AFTERTHEME\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:AFTERTHEME"));

            if (_tray is null || !_tray.IsRegistered) throw new InvalidOperationException("The tray icon was not registered with Windows.");
            // Its entry was found and placed: on the taskbar, unless someone had hidden this copy's icon before.
            if (await _trayPlacement is not (0 or 1))
                throw new InvalidOperationException("SIC did not find its tray icon's entry under HKCU\\" + TrayPlacement.SettingsKey + " to put it on the taskbar.");
            _tray.RequestWindowClose();
            await WaitForAsync(() => Task.FromResult(!AppWindow.IsVisible));
            var closeToTray = pane.IsRunning && _tray.IsRegistered;
            if (!closeToTray) throw new InvalidOperationException("Closing the window did not preserve its running terminal in the tray.");

            // A start made while the window is in the tray. Its page loads hidden and runs no animation frames; it used to wait
            // for one before reporting ready, so the start timed out unless the window came back first. The load budget is
            // cut to eight seconds so that behaviour would fail here rather than after 25.
            var trayStartSaved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · start in the tray", data, "");
            var trayStartHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), trayStartSaved.Title, [trayStartSaved]));
            var trayStartPane = trayStartHost.Panes[0];
            _notice.IsOpen = false;
            TerminalView.LoadBudget = TimeSpan.FromSeconds(8);
            var trayStart = ResumePaneAsync(trayStartPane, new("the verification's start in the tray", DateTimeOffset.UtcNow));
            await Task.WhenAny(trayStart, Task.Delay(TimeSpan.FromSeconds(10)));
            TerminalView.LoadBudget = TimeSpan.FromSeconds(25);
            var hiddenWindowStart = trayStart.IsCompleted && trayStartPane.IsRunning && !AppWindow.IsVisible && !_notice.IsOpen;
            var trayStartNotice = _notice.IsOpen ? _notice.Message ?? "" : "";
            AppWindow.Show(false);
            await trayStart;
            if (trayStartPane.IsRunning)
            {
                await WaitForAsync(async () => (await trayStartPane.Terminal!.ReadScreenAsync()).Contains("READY"));
                await trayStartPane.Terminal!.SendFromRendererAsync("EXIT\r");
                await trayStartPane.Terminal.StopAsync();
            }
            await CloseTabAsync(trayStartHost.Item);
            _tabs.SelectedItem = shellHost.Item;
            _notice.IsOpen = false;
            if (!hiddenWindowStart) throw new InvalidOperationException($"A start made while the window was in the tray did not start until it was shown: status={trayStartPane.Status} notice='{trayStartNotice}'.");
            await terminal.SendFromRendererAsync("PING:AFTERTRAY\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:AFTERTRAY"));
            // Settings → Close and minimize (docs/decisions/close-and-minimize.md). At its default, Close and minimize to tray,
            // minimizing hides the window with its terminal running, as closing did above. With Close to tray, minimize to
            // taskbar, and with Close quits, minimize to taskbar, minimizing leaves the window minimized on the taskbar; where
            // each one's close goes is read from the choice the window's Closing takes rather than made, since a quit would end
            // the run. Each choice is saved, and the default is put back.
            _tray.RequestMinimize();
            await WaitForAsync(() => Task.FromResult(!AppWindow.IsVisible));
            var minimizeToTray = pane.IsRunning && !AppWindow.IsVisible && CloseGoesToTray;
            ShowFromTray();
            await WaitForAsync(() => Task.FromResult(AppWindow.IsVisible));
            static bool Minimized(Microsoft.UI.Windowing.AppWindow window) => window.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
            var closeAndMinimizeMisses = new List<string>();
            foreach (var choice in new[] { CloseAndMinimize.CloseToTray, CloseAndMinimize.Quit })
            {
                SetCloseAndMinimize(choice);
                _tray.RequestMinimize();
                var minimized = await EventuallyAsync(() => Minimized(AppWindow)) && AppWindow.IsVisible && pane.IsRunning;
                var closesToTray = CloseGoesToTray;
                ShowFromTray();
                await WaitForAsync(() => Task.FromResult(!Minimized(AppWindow) && AppWindow.IsVisible));
                if (!minimized || closesToTray != (choice == CloseAndMinimize.CloseToTray) || SavedPreferences(() => new AppPreferences()).CloseAndMinimize != choice)
                    closeAndMinimizeMisses.Add($"{choice}: minimized={minimized} closes to the tray={closesToTray}");
            }
            SetCloseAndMinimize(CloseAndMinimize.Tray);
            var closeAndMinimize = closeAndMinimizeMisses.Count == 0 && CloseGoesToTray;
            await terminal.SendFromRendererAsync("PING:AFTERMINIMIZE\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PONG:AFTERMINIMIZE"));
            if (!minimizeToTray) throw new InvalidOperationException("Minimizing with Close and minimize at its default did not hide the window while keeping its terminal.");
            if (!closeAndMinimize) throw new InvalidOperationException("Settings → Close and minimize was wrong: " + string.Join("; ", closeAndMinimizeMisses) + ".");
            var original = await terminal.GetDimensionsAsync();
            _options.Trace($"before-resize root={_root.ActualWidth}x{_root.ActualHeight} terminal={terminal.ActualWidth}x{terminal.ActualHeight} cells={original.Columns}x{original.Rows}");
            terminal.Width = 410;
            await WaitForAsync(async () => (await terminal.GetDimensionsAsync()).Columns != original.Columns);
            var resized = await terminal.GetDimensionsAsync();
            _options.Trace($"renderer-resized {resized.Columns}x{resized.Rows}");
            await terminal.SendFromRendererAsync("SIZE\r");
            var reply = $"COLS:{resized.Columns};ROWS:{resized.Rows}";
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains(reply));
            _options.Trace("native-size-reply");
            terminal.Width = double.NaN;
            await WaitForAsync(async () => (await terminal.GetDimensionsAsync()).Columns == original.Columns);
            _options.Trace("renderer-width-restored");
            // Rows, with the screen full and scrollback above it: after a shorter pane and back, the renderer must show each line on
            // the row the console host has it on. The host adds a taller pane's rows at the bottom and keeps its cursor where it
            // was; xterm does the same only when told the host is ConPTY (windowsPty), and otherwise pulls the scrollback down into
            // them, so the host's next absolute move (its echo of a typed line, PSReadLine's redraws) lands mid-screen over old
            // lines (seen October 6, 2026, with windowsPty withheld).
            await terminal.SendFromRendererAsync("FILL\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("FILL:80"));
            var fullHeight = await terminal.GetDimensionsAsync();
            // The resize reaches the host on a pipe of its own, so each report is asked for until the host has the new height.
            async Task<(int Row, int Rows, string Nonce)> HostCursorAsync(int rows)
            {
                (int Row, int Rows, string Nonce) report = default;
                for (var attempt = 0; attempt < 20 && report.Rows != rows; attempt++)
                {
                    if (attempt > 0) await Task.Delay(150);
                    var nonce = "C" + Guid.NewGuid().ToString("N")[..12];
                    await terminal.SendFromRendererAsync("CURSOR:" + nonce + "\r");
                    await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains(";" + nonce));
                    var line = System.Text.RegularExpressions.Regex.Match(await terminal.ReadScreenAsync(), @"ROW:(\d+);TOP:(\d+);ROWS:(\d+);" + nonce);
                    report = (int.Parse(line.Groups[1].Value) - int.Parse(line.Groups[2].Value), int.Parse(line.Groups[3].Value), nonce);
                }
                return report;
            }
            terminal.Height = Math.Max(160, terminal.ActualHeight / 2);
            await WaitForAsync(async () => (await terminal.GetDimensionsAsync()).Rows < fullHeight.Rows);
            var shortRows = (await terminal.GetDimensionsAsync()).Rows;
            var beforeGrow = await HostCursorAsync(shortRows);
            terminal.Height = double.NaN;
            await WaitForAsync(async () => (await terminal.GetDimensionsAsync()).Rows == fullHeight.Rows);
            await Task.Delay(500);
            // Where the renderer shows the last line printed before the pane grew, read before anything else is printed; then where
            // the host puts the next one. That one comes two rows below it (the typed line's echo, then the reply).
            var referenceDrawn = await terminal.RowOfAsync(";" + beforeGrow.Nonce);
            var afterGrow = await HostCursorAsync(fullHeight.Rows);
            var afterDrawn = await terminal.RowOfAsync(";" + afterGrow.Nonce);
            _options.Trace($"cursor-rows short={shortRows} before={beforeGrow.Row} referenceDrawn={referenceDrawn} after={afterGrow.Row}/{afterGrow.Rows} afterDrawn={afterDrawn}");
            var cursorRowsAgree = beforeGrow.Rows == shortRows && afterGrow.Rows == fullHeight.Rows && referenceDrawn == afterGrow.Row - 2 && afterDrawn == afterGrow.Row;
            if (!cursorRowsAgree) throw new InvalidOperationException($"After a pane of {shortRows} rows grew back to {fullHeight.Rows}, the renderer showed the last line printed before it on row {referenceDrawn}, where the console host had it on row {afterGrow.Row - 2} (host rows {beforeGrow.Rows}, then {afterGrow.Rows}; the next line drawn on {afterDrawn}, host {afterGrow.Row}).");
            // Nothing the console host and the renderer say to each other shows on screen: the host's own questions (DA1 at start,
            // the cursor after a read), xterm's answers, and the focus reports the host asks for (?1004h) as the pane gains and
            // loses the keyboard, which the smoke has done many times by now. Typed into the fixture's line they would be echoed.
            var answersScreen = await terminal.ReadScreenAsync();
            var queryAnswersHidden = !answersScreen.Contains('\u001b') && !answersScreen.Contains("^[", StringComparison.Ordinal) && !answersScreen.Contains("[?", StringComparison.Ordinal)
                && !answersScreen.Contains("[I", StringComparison.Ordinal) && !answersScreen.Contains("[O", StringComparison.Ordinal)
                && !System.Text.RegularExpressions.Regex.IsMatch(answersScreen, @"\d+;\d+R|\d;\d+c");
            if (!queryAnswersHidden) throw new InvalidOperationException("A query or its answer between the console host and the renderer showed in the terminal: " + JsonSerializer.Serialize(answersScreen[^Math.Min(answersScreen.Length, 2000)..]));
            var image = Path.Combine(data, "terminal.png");
            await terminal.CaptureAsync(image);
            var shellImage = await CaptureShellAsync(Path.Combine(data, "shell-native.png"), terminal);

            // Hermes's pet as an image (docs/decisions/pet-images.md), last on this pane because it clears the screen. The fixture's
            // PET verb writes a kitty transmission and its 4×2 placeholder grid through the real pseudoconsole; the packaged console
            // host passes the APC through, the splitter takes it out, and the page draws the image over the cells. Windows' own host
            // drops the APC, so under the fallback this fails (petConsoleHost records which ran). The capture must show the image's
            // colour inside the box and the background just outside it; then a clear hides the overlay, the delete Hermes sends when
            // its pet goes away forgets the image, and with Pet images off the same output leaves no image and still renders.
            async Task<JsonElement> PetWhen(Func<JsonElement, bool> condition)
            {
                var check = await terminal.ReadPetImagesAsync();
                for (var waited = 0; waited < 8000 && !condition(check); waited += 75)
                {
                    await Task.Delay(75);
                    check = await terminal.ReadPetImagesAsync();
                }
                return check;
            }
            static bool Holds(JsonElement check) => check.GetProperty("ids").EnumerateArray().Any(id => id.GetUInt32() == SmokePetId);
            static bool Hidden(JsonElement check) => check.GetProperty("canvas").GetProperty("hidden").GetBoolean();
            var petConsoleHost = terminal.PseudoconsoleHost;
            var petNonce = Guid.NewGuid().ToString("N")[..12];
            await terminal.SendFromRendererAsync("PET:" + petNonce + "\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PETEND:" + petNonce));
            var petShown = await PetWhen(check => Holds(check) && check.GetProperty("placementsDrawn").GetInt32() == 1 && check.GetProperty("runsDrawn").GetInt32() == 2 && !Hidden(check));
            var petRuns = petShown.GetProperty("runs").EnumerateArray().Where(run => run.GetProperty("id").GetUInt32() == SmokePetId)
                .Select(run => (X: run.GetProperty("x").GetDouble(), Y: run.GetProperty("y").GetDouble(), Width: run.GetProperty("width").GetDouble(), Height: run.GetProperty("height").GetDouble())).ToArray();
            if (!Holds(petShown) || petRuns.Length != 2)
                throw new InvalidOperationException($"The fixture's pet image was not drawn over its two rows of placeholder cells (console host {petConsoleHost}; Windows' own drops the image): {petShown}");
            // The box in CSS pixels, and the colours a capture shows inside it (the middle of each row) and 3 px outside each edge.
            var (petLeft, petTop) = (petRuns.Min(run => run.X), petRuns.Min(run => run.Y));
            var (petRight, petBottom) = (petRuns.Max(run => run.X + run.Width), petRuns.Max(run => run.Y + run.Height));
            var (petMiddleX, petMiddleY, petRow) = ((petLeft + petRight) / 2, (petTop + petBottom) / 2, petRuns[0].Height);
            var petImage = Path.Combine(data, "pet-image.png");
            await terminal.CaptureAsync(petImage);
            var petPixels = await ReadPixelsAsync(petImage);
            var petScale = petPixels.Width / petShown.GetProperty("viewport").GetProperty("width").GetDouble();
            var petInside = new[] { ColorAt(petPixels, petScale, petMiddleX, petTop + petRow / 2), ColorAt(petPixels, petScale, petMiddleX, petTop + petRow * 1.5) };
            var petOutside = new[]
            {
                ColorAt(petPixels, petScale, petLeft - 3, petMiddleY), ColorAt(petPixels, petScale, petRight + 3, petMiddleY),
                ColorAt(petPixels, petScale, petMiddleX, petTop - 3), ColorAt(petPixels, petScale, petMiddleX, petBottom + 3)
            };
            var petImageDrawn = petConsoleHost == ConsoleHost.Packaged && petInside.All(color => ColorsNear(color, SmokePetColor))
                && petOutside.All(color => ColorsNear(color, FixedTerminalBackground));
            if (!petImageDrawn) throw new InvalidOperationException($"The pet image was not drawn in its box alone: inside [{string.Join(", ", petInside)}] (expected {SmokePetColor}), outside [{string.Join(", ", petOutside)}] (expected {FixedTerminalBackground}), box {petLeft:0.#},{petTop:0.#}–{petRight:0.#},{petBottom:0.#} at {petScale:0.##}, host {petConsoleHost}.");
            // Cleared, as an agent clears its screen: the placeholders are gone, so the overlay is hidden though the image is still held.
            await terminal.SendFromRendererAsync("CLS\r");
            var petAfterClear = await PetWhen(check => Hidden(check) && check.GetProperty("placeholderRows").GetInt32() == 0);
            var petImageCleared = Hidden(petAfterClear) && petAfterClear.GetProperty("placeholderRows").GetInt32() == 0 && Holds(petAfterClear);
            if (!petImageCleared) throw new InvalidOperationException("Clearing the screen and scrollback did not hide the pet image: " + petAfterClear);
            // The delete Hermes writes when its pet goes away (a=d,d=i), through the splitter's kitty-delete.
            await terminal.SendFromRendererAsync("PETDEL\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PETDEL:DONE"));
            var petAfterDelete = await PetWhen(check => !Holds(check));
            var petImageDeleted = !Holds(petAfterDelete) && petAfterDelete.GetProperty("frames").GetInt32() == 0;
            if (!petImageDeleted) throw new InvalidOperationException("The kitty delete did not remove the pet image: " + petAfterDelete);
            // Settings → Pet images off, through the Settings code path, on the running pane: the output goes through untouched,
            // xterm swallows the APC unread (none of it on screen), the cells show what xterm draws for them, and the page holds and
            // draws nothing. Left off for the restore stage and for the pane started next.
            await ApplyTerminalOptionsAsync(preferences => preferences.PetImages = false);
            var petOffNonce = Guid.NewGuid().ToString("N")[..12];
            await terminal.SendFromRendererAsync("PET:" + petOffNonce + "\r");
            await WaitForAsync(async () => (await terminal.ReadScreenAsync()).Contains("PETEND:" + petOffNonce));
            await Task.Delay(300);
            var petOff = await terminal.ReadPetImagesAsync();
            var petOffScreen = await terminal.ReadScreenAsync();
            var petOffImage = Path.Combine(data, "pet-images-off.png");
            await terminal.CaptureAsync(petOffImage);
            var petOffRow = await terminal.RowOfAsync("PETBOX:" + petOffNonce);
            var petOffPixels = await ReadPixelsAsync(petOffImage);
            var petOffCell = (Width: petOff.GetProperty("cell").GetProperty("width").GetDouble(), Height: petOff.GetProperty("cell").GetProperty("height").GetDouble());
            var petOffScreenAt = (Left: petOff.GetProperty("screen").GetProperty("left").GetDouble(), Top: petOff.GetProperty("screen").GetProperty("top").GetDouble());
            var petOffScale = petOffPixels.Width / petOff.GetProperty("viewport").GetProperty("width").GetDouble();
            // Where the image would be: the middle of the grid's first row (two columns in, two rows under the PETBOX line); only
            // checked not to be the image's colour, since what xterm draws for a placeholder depends on its fonts.
            var petOffInside = ColorAt(petOffPixels, petOffScale, petOffScreenAt.Left + petOffCell.Width * 4, petOffScreenAt.Top + petOffCell.Height * (petOffRow + 2.5));
            var petImagesOff = !petOff.GetProperty("enabled").GetBoolean() && petOff.GetProperty("ids").GetArrayLength() == 0 && petOff.GetProperty("frames").GetInt32() == 0
                && Hidden(petOff) && petOff.GetProperty("placeholderRows").GetInt32() == 0 && petOffRow >= 0
                && petOffScreen.Contains("PETBOX:" + petOffNonce) && petOffScreen.Contains("\U0010EEEE", StringComparison.Ordinal)
                && !petOffScreen.Contains("_Ga=", StringComparison.Ordinal) && !petOffScreen.Contains(SmokePetPngTail[..12], StringComparison.Ordinal)
                && !ColorsNear(petOffInside, SmokePetColor) && !SavedPreferences(() => new AppPreferences()).PetImages;
            if (!petImagesOff) throw new InvalidOperationException($"With Pet images off the fixture's pet left an image, or its output did not render as text with the APC swallowed: row {petOffRow}, box colour {petOffInside}, {petOff}");
            await terminal.SendFromRendererAsync("EXIT\r");
            await terminal.StopAsync();
            ApplyTheme("fluent-light");
            await SetPanelOpen(false);
            var lightDescriptor = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · light startup", data, "");
            var lightPane = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), lightDescriptor.Title, [lightDescriptor])).Panes[0];
            await lightPane.StartAsync(CreateShellRequest(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", SmokeScript], data), SmokeStart);
            var lightStarted = await lightPane.Terminal!.ReadAppearanceAsync();
            var lightStartupApplied = lightStarted.Background == FixedTerminalBackground && lightPane.IsRunning && (await lightPane.Terminal.ReadOptionsAsync()).CursorStyle == "block";
            if (!lightStartupApplied) throw new InvalidOperationException("A new terminal started under the light theme did not start with the fixed palette and the saved options.");
            // Started with Pet images off (left so above): the page was told before the first output and draws nothing.
            var lightPet = await lightPane.Terminal.ReadPetImagesAsync();
            var petImagesOffAtStart = !lightPet.GetProperty("enabled").GetBoolean() && lightPet.GetProperty("frames").GetInt32() == 0;
            if (!petImagesOffAtStart) throw new InvalidOperationException("A terminal started with Pet images off did not start with its image overlay off: " + lightPet);
            var gracefulExit = await lightPane.TryExitAsync(TimeSpan.FromSeconds(8)) && !lightPane.IsRunning;
            if (!gracefulExit) throw new InvalidOperationException("Sending the shell its exit command did not end the process within the budget.");

            // Always exit cleanly, turned on through the Settings code path and saved: closing a tab with a live shell asks
            // nothing, a second close while the first runs returns at once, and the shell ends on its own exit command. Left on
            // for the restore stage.
            SetAlwaysExitCleanly(true);
            SessionPane LiveShell(string title) => CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), title, [new SavedPane(Guid.NewGuid().ToString("N"), "shell", title, data, "")])).Panes[0];
            var cleanPane = LiveShell("PowerShell · clean close");
            await cleanPane.StartAsync(CreateShellRequest(SmokeShellArguments, data), SmokeStart);
            await WaitForAsync(async () => (await cleanPane.Terminal!.ReadScreenAsync()).Contains("READY"));
            var cleanTab = HostOf(cleanPane)!.Item;
            var questionsBefore = _closeQuestions;
            var cleanClosing = CloseTabAsync(cleanTab);
            var repeatReturned = CloseTabAsync(cleanTab).IsCompleted && cleanPane.IsRunning;
            await cleanClosing;
            var askedOnClose = _closeQuestions != questionsBefore;
            var alwaysExitCleanly = repeatReturned && !askedOnClose && cleanPane.ExitedOnRequest && !cleanPane.IsRunning && !_open.ContainsKey(cleanTab)
                && SavedPreferences(() => new AppPreferences()).AlwaysExitCleanly;
            if (!alwaysExitCleanly) throw new InvalidOperationException($"Closing a live tab with Always exit cleanly on did not end it by its exit command without asking: repeatReturned={repeatReturned} asked={askedOnClose} exitedOnRequest={cleanPane.ExitedOnRequest} open={_open.ContainsKey(cleanTab)}.");

            // The quit dialog, driven the way Quit drives it but without quitting: it lists the live session with only Force quit
            // now to press, starts with the keyboard focus on its content rather than on that button, closes itself once the
            // session has exited, and says so on the way out.
            var quitPane = LiveShell("PowerShell · quit dialog");
            await quitPane.StartAsync(CreateShellRequest(SmokeShellArguments, data), SmokeStart);
            await WaitForAsync(async () => (await quitPane.Terminal!.ReadScreenAsync()).Contains("READY"));
            var quitting = ExitWithProgressAsync([quitPane]);
            var progress = _exitProgress;
            var quitDialogListed = progress is { Rows.Children.Count: 1 } && progress.Dialog is { SecondaryButtonText: "Force quit now", PrimaryButtonText: "", CloseButtonText: "" };
            var quitDialogOpened = await EventuallyAsync(() => _dialogsOpen == 1);
            var quitFocusOnContent = await EventuallyAsync(() => ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(_root.XamlRoot), progress?.FocusHost));
            var quitFocused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(_root.XamlRoot)?.GetType().Name ?? "nothing";
            var quitLeftRunning = await quitting;
            var quitDialog = quitDialogListed && quitDialogOpened && quitFocusOnContent && quitLeftRunning == 0 && quitPane.ExitedOnRequest && !quitPane.IsRunning
                && progress!.Summary.Text == ExitProgress.Summary(1, 1, 0) && _exitProgress is null && _dialogsOpen == 0;
            if (!quitDialog) throw new InvalidOperationException($"The quit dialog did not list the live session, keep the focus off Force quit now, close itself once it exited, and say so: listed={quitDialogListed} opened={quitDialogOpened} focusOnContent={quitFocusOnContent} focused={quitFocused} leftRunning={quitLeftRunning} summary='{progress?.Summary.Text}' open={_dialogsOpen}.");
            await CloseTabAsync(HostOf(quitPane)!.Item);
            // A tab saved before there were trees (four panes, a column and a row ratio) opens as the tree that tiles it the same
            // way, and is saved as that tree. A six-pane tree with two named panes is made for the restore stage. Neither is
            // selected, so their tiling is read once laid out there.
            var gridPanes = Enumerable.Range(1, 4).Select(index => new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · grid " + index, data, "")).ToList();
            var gridHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "PowerShell · grid", gridPanes, SplitRatio: 0.4, RowRatio: 0.65), select: false);
            var gridAsTree = gridHost.Saved.Layout == PaneLayout.Legacy(gridPanes.Select(item => item.Id).ToList(), 0.4, 0.65) && gridHost.Saved.SplitRatio is null && gridHost.Saved.RowRatio is null
                && gridHost.Panes.Select(item => item.Descriptor.Id).SequenceEqual([gridPanes[0].Id, gridPanes[1].Id, gridPanes[3].Id, gridPanes[2].Id]) && PanesTiled(gridHost);
            if (!gridAsTree) throw new InvalidOperationException("A four-pane tab saved as a grid did not open as the tree of the same shape.");
            var treePanes = Enumerable.Range(1, 6).Select(index => new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · tree " + index, data, "", CustomTitle: index switch { 1 => "Tree first", 4 => "Tree fourth", _ => null })).ToList();
            var treeLayout = PaneLayout.SplitNode(PaneSplit.Columns, 0.4,
                PaneLayout.SplitNode(PaneSplit.Rows, 0.65, PaneLayout.Leaf(treePanes[0].Id), PaneLayout.SplitNode(PaneSplit.Columns, 0.5, PaneLayout.Leaf(treePanes[1].Id), PaneLayout.Leaf(treePanes[2].Id))),
                PaneLayout.SplitNode(PaneSplit.Rows, 0.5, PaneLayout.Leaf(treePanes[3].Id), PaneLayout.SplitNode(PaneSplit.Rows, 0.3, PaneLayout.Leaf(treePanes[4].Id), PaneLayout.Leaf(treePanes[5].Id))));
            var treeHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "PowerShell · tree", treePanes, Layout: treeLayout), select: false);
            if (!ReferenceEquals(treeHost.Saved.Layout, treeLayout) || treeHost.Title.Text != "Tree first" || treeHost.Panes[3].NameText != "Tree fourth" || !PanesTiled(treeHost))
                throw new InvalidOperationException("A six-pane tab created from a saved tree was not tiled by it or did not show its pane names.");
            // The tree tab pinned whole, and left so for the restore stage: its group is the tab as it would be saved, the tab shows
            // the pin, the notice names it with its count, and the group is in settings.json.
            await TogglePinTabAsync(treeHost.Item);
            var treeGroup = TabGroups.Find(_preferences.TabGroups, treeHost.Saved.Id);
            var treePinned = treeGroup is { Panes.Count: 6 } && TabGroups.Same(treeGroup, SavedNow(treeHost)) && treeHost.Panes.All(pane => pane.PinShown) && IsTabPinned(treeHost)
                && LastNotice is { Brief: true, Title: "Pinned to the top of Sessions", Message: "Tree first · 6 panes" }
                && SavedPreferences(() => new AppPreferences()).TabGroups.Any(group => group.Id == treeHost.Saved.Id && group.Panes.Count == 6);
            if (!treePinned) throw new InvalidOperationException($"Pinning the six-pane tab did not keep it as a group with its tree: panes={treeGroup?.Panes.Count} pins={treeHost.Panes.Count(pane => pane.PinShown)} notice='{LastNotice?.Title}: {LastNotice?.Message}'.");
            _notice.IsOpen = false;

            // The width the restore stage expects, set while the panel is closed: stored and given to the body, the slot left shut.
            var panelResized = panelWidthApplied && panelWidthClamped && !_panelOpen && SetPanelWidth(SmokePanelWidth) == SmokePanelWidth
                && _panel.Width == 0 && _panelEdge.Visibility == Visibility.Collapsed && _panelBody.Width == SmokePanelWidth;
            if (!panelResized) throw new InvalidOperationException("Setting the panel width while the panel was closed opened it or did not store the width.");

            // Last, because everything above is measured at the fixed 1440×900. The tracking a normal launch runs, fed by real
            // window moves: a move is saved, a maximize keeps the restored bounds, a minimize is ignored, a restore clears the flag.
            TrackWindowPlacement();
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
            bool Settled(WindowPlacement expected) => _preferences.Window == expected && !_savePreferences.Pending;
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(SmokePlacement.X, SmokePlacement.Y, SmokePlacement.Width, SmokePlacement.Height));
            await WaitForAsync(() => Task.FromResult(Settled(SmokePlacement)));
            var movedSaved = SavedPreferences(() => new AppPreferences()).Window == SmokePlacement;
            // Full screen and back with the tracking on, as F11 is pressed in a normal launch: the window returns to the bounds it
            // had, and what is saved is those bounds, not the display's, which the size changes of the way in and out arrive with.
            SetFullScreen(true);
            var fullForPlacement = await EventuallyAsync(() => _fullScreen && AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            SetFullScreen(false);
            var fullScreenReturned = fullForPlacement && await EventuallyAsync(() => !_fullScreen && Settled(SmokePlacement)
                && AppWindow.Position.X == SmokePlacement.X && AppWindow.Position.Y == SmokePlacement.Y && AppWindow.Size.Width == SmokePlacement.Width && AppWindow.Size.Height == SmokePlacement.Height);
            if (!fullScreenReturned)
                throw new InvalidOperationException($"Full screen and back with placement tracked did not return to the window's bounds: entered={fullForPlacement} placement={_preferences.Window} "
                    + $"window={AppWindow.Position.X},{AppWindow.Position.Y} {AppWindow.Size.Width}x{AppWindow.Size.Height} restored={_restoredBounds}.");
            // Where the New button and the rail's bottom were before the maximize: areas left over from the smaller window
            // would still hold a hole at the one and end at the other.
            var newButtonBefore = _railItems["new"].Button.TransformToVisual(_rail).TransformPoint(new(0, 20)).Y;
            var railBefore = _rail.ActualHeight;
            presenter.Maximize();
            await WaitForAsync(() => Task.FromResult(Settled(SmokePlacement with { Maximized = true })));
            var maximizedSaved = SavedPreferences(() => new AppPreferences()).Window == SmokePlacement with { Maximized = true } && AppWindow.Size.Width != SmokePlacement.Width;
            // Maximized, the rail is taller and its lower buttons have moved: the areas follow.
            var dragMaximized = await EventuallyAsync(() => _rail.ActualHeight > railBefore + 40 && IsDragPoint(_rail, RailWidth / 2, newButtonBefore)
                && IsDragPoint(_rail, RailWidth / 2, _rail.ActualHeight - 3) && IsDragPoint(_rail, RailWidth / 2, RailGap()) && !IsDragPoint(_railItems["settings"].Button, 20, 20));
            if (!dragMaximized) throw new InvalidOperationException($"The drag areas did not follow the maximized window: {DragAreaText(_dragRects)}.");
            dragRegions &= dragMaximized;
            presenter.Minimize();
            await WaitForAsync(() => Task.FromResult(presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized));
            await Task.Delay(400);
            var minimizeIgnored = _preferences.Window == SmokePlacement with { Maximized = true };
            // Un-minimizing may land back in the maximized state first; either way one more restore ends at the saved bounds.
            presenter.Restore();
            await WaitForAsync(() => Task.FromResult(presenter.State != Microsoft.UI.Windowing.OverlappedPresenterState.Minimized));
            if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized) presenter.Restore();
            await WaitForAsync(() => Task.FromResult(Settled(SmokePlacement)));
            var windowPlacementTracked = movedSaved && maximizedSaved && minimizeIgnored && SavedPreferences(() => new AppPreferences()).Window == SmokePlacement
                && AppWindow.Position.X == SmokePlacement.X && AppWindow.Position.Y == SmokePlacement.Y && AppWindow.Size.Width == SmokePlacement.Width && AppWindow.Size.Height == SmokePlacement.Height;
            if (!windowPlacementTracked) throw new InvalidOperationException($"Window placement was not tracked: moved={movedSaved} maximized={maximizedSaved} minimizeIgnored={minimizeIgnored} saved={_preferences.Window}.");
            // Open in SicTerm: a second SicTerm.exe --open for this data folder finds this one holding it, hands the folder over in a
            // window message and exits; the New session dialog opens with the folder in its box. With --agent shell a PowerShell
            // pane starts in the folder without the dialog. A folder that does not exist gets a held notice and no dialog. The
            // registry is never touched: this copy runs with its own data folder, and the switch in Settings is disabled for it.
            var openFolder = Path.Combine(data, "open-in-sic");
            Directory.CreateDirectory(openFolder);
            async Task<int> OpenFromSecondProcessAsync(params string[] arguments)
            {
                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                foreach (var argument in new[] { "--data-dir", data }.Concat(arguments)) start.ArgumentList.Add(argument);
                using var second = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("The second SicTerm.exe did not start.");
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await second.WaitForExitAsync(budget.Token); }
                catch (OperationCanceledException) { second.Kill(true); throw new InvalidOperationException("The second SicTerm.exe --open did not exit within 20 seconds."); }
                return second.ExitCode;
            }
            var openExit = await OpenFromSecondProcessAsync("--open", openFolder);
            var openDialogShown = await EventuallyAsync(() => _newSessionDialog is not null && _newSessionFolder?.Text == openFolder && _dialogsOpen == 1);
            var openFolderReceived = openExit == 0 && openDialogShown && AppWindow.IsVisible;
            if (!openFolderReceived) throw new InvalidOperationException($"A second SicTerm.exe --open did not open the New session dialog with its folder: exit={openExit} dialog={openDialogShown} folder='{_newSessionFolder?.Text}' visible={AppWindow.IsVisible}.");
            _newSessionDialog!.Hide();
            await WaitForAsync(() => Task.FromResult(_dialogsOpen == 0 && _newSessionDialog is null));
            var openAgentExit = await OpenFromSecondProcessAsync("--open", openFolder, "--agent", "shell");
            SessionPane? openPane = null;
            await WaitForAsync(() => Task.FromResult((openPane = AllPanes.FirstOrDefault(pane => pane.Descriptor.AgentId == "shell" && pane.Descriptor.WorkingDirectory == openFolder)) is { IsRunning: true }));
            var openAgentStarted = openAgentExit == 0 && openPane is { IsRunning: true } && _dialogsOpen == 0 && ReferenceEquals(_activePane, openPane);
            if (!openAgentStarted) throw new InvalidOperationException($"A second SicTerm.exe --open --agent shell did not start a PowerShell pane in its folder: exit={openAgentExit} running={openPane?.IsRunning} active={ReferenceEquals(_activePane, openPane)}.");
            var openMissingFolder = Path.Combine(data, "open-in-sic-missing");
            var openMissingExit = await OpenFromSecondProcessAsync("--open", openMissingFolder);
            var openMissingNoticed = await EventuallyAsync(() => LastNotice is { Title: OpenTitle, Brief: false, Severity: InfoBarSeverity.Warning } notice && notice.Message.Contains(openMissingFolder, StringComparison.Ordinal));
            var openMissingRefused = openMissingExit == 0 && openMissingNoticed && _dialogsOpen == 0 && _newSessionDialog is null;
            if (!openMissingRefused) throw new InvalidOperationException($"A second SicTerm.exe --open with a missing folder did not leave a held notice naming it: exit={openMissingExit} notice='{LastNotice?.Message}' dialogs={_dialogsOpen}.");
            if (ReferenceEquals(_activePane, openPane) && HostOf(openPane!) is { } openHost) await CloseTabAsync(openHost.Item);

            // Windows 11's own menu: the command DLL and the signed identity package were published beside the exe, and the DLL,
            // loaded here as Explorer would load it and pointed at a scratch menu key, lists the menu SIC lays out: Open in SicTerm
            // with subcommands, New session… first with a separator after it, the four fixture agents and PowerShell with their
            // icons, every command carrying the item placeholder; and an empty key hides the command. The menu written for this copy
            // reads back as this copy's, from New session's command, since its Icon is the mark. The real menu key and the package
            // registration are not touched: the scratch key is deleted again.
            var exeDirectory = AppContext.BaseDirectory;
            // A build that left both out on purpose (Publish-App.ps1 -SkipIdentity: a PC without the C++ tools or the signing
            // sign-in, as anyone building from source) skips the menu's checks below, and the report says so (identitySkipped).
            var identitySkipped = _options.WithoutIdentity;
            var explorerCommandBuilt = !identitySkipped && File.Exists(Path.Combine(exeDirectory, ExplorerMenu.CommandFileName));
            if (!identitySkipped && !explorerCommandBuilt) throw new InvalidOperationException("SicExplorerCommand.dll was not published beside the exe; the C++ build is part of Publish-App.ps1.");
            var explorerPackageSigned = !identitySkipped && ExplorerCommandProbe.PackageSignedBy(exeDirectory, ExplorerMenu.PackagePublisher) == true;
            if (!identitySkipped && !explorerPackageSigned) throw new InvalidOperationException("SicTerm.Identity.msix is missing beside the exe or is not signed by " + ExplorerMenu.PackagePublisher + ".");
            // Run at Startup (November 12, 2026), under a scratch name so the person's own entry is never touched: on writes this exe
            // and reads back as on, off removes it.
            var scratchStartup = "SicTerm-smoke-" + Guid.NewGuid().ToString("N");
            try
            {
                StartupRegistration.Enable(Environment.ProcessPath!, scratchStartup);
                var startupOn = StartupRegistration.IsOn(Environment.ProcessPath!, scratchStartup) && !StartupRegistration.IsOn(Environment.ProcessPath + ".other", scratchStartup);
                StartupRegistration.Disable(scratchStartup);
                if (!startupOn || StartupRegistration.Command(scratchStartup) is not null) throw new InvalidOperationException("Run at Startup did not write this exe under the Run key, or did not remove it.");
            }
            finally { StartupRegistration.Disable(scratchStartup); }
            var scratchMenuKey = @"Software\Super-Intelligent-Chat\Smoke\" + Guid.NewGuid().ToString("N");
            bool explorerMenuOwned = false, explorerCommandEnumerated = false;
            if (!identitySkipped)
            try
            {
                var menuItems = ExplorerMenuRegistration.Items(Environment.ProcessPath!, _preferences.Agents, IconSourceFor, LibraryIconFor);
                // An agent whose tile is its own icon from something other than an exe (Hermes's icon.ico, the Codex app's logo) is
                // named from SIC's icon library, which the shell reads at every size the menu asks for.
                var libraryAgents = menuItems.Take(4).Where(item => IconSourceFor(item.Id) is { } found && !found.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
                var menuLibrary = libraryAgents.All(item => LibraryIconFor(item.Id) is { } icon && item.IconPath == icon
                    && AgentIconCache.MenuFrames.All(size => ExeIcons.ReadReference(icon, size) is { } pixels && pixels.Length == size * size * 4));
                if (!menuLibrary) throw new InvalidOperationException("An agent's own icon from outside an exe is not in the menu's icon library: "
                    + string.Join(", ", libraryAgents.Select(item => item.Id + "=" + item.IconPath + " library=" + (LibraryIconFor(item.Id) ?? "none"))) + ".");
                var markIcon = ExplorerMenuRegistration.MarkIcon(Environment.ProcessPath!);
                ExplorerMenuRegistration.Write(ExplorerMenu.PackageEntries(Environment.ProcessPath!, menuItems, scratchMenuKey, markIcon), scratchMenuKey);
                var readBack = ExplorerMenuRegistration.ReadExecutable(scratchMenuKey);
                explorerMenuOwned = string.Equals(readBack, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
                if (!explorerMenuOwned) throw new InvalidOperationException($"The menu written for this copy reads back as pointing at '{readBack}', not at '{Environment.ProcessPath}'.");
                var commandDll = Path.Combine(exeDirectory, ExplorerMenu.CommandFileName);
                var command = ExplorerCommandProbe.Read(commandDll, scratchMenuKey);
                var hiddenWithoutMenu = ExplorerCommandProbe.Read(commandDll, scratchMenuKey + "-none");
                var expectedTitles = new[] { ExplorerMenu.NewSessionTitle }.Concat(menuItems.Select(item => item.Name)).ToArray();
                // The DLL answers its own resources for the mark (the set for the menus' mode now) and the agents' tiles, "path,0" for an exe.
                var markResource = commandDll + ",-" + ExplorerMenu.CommandIconIds[ExplorerMenu.MarkFileFor(ShellIcons.LightMenus())];
                string ExpectedIcon(string iconPath) => ExplorerMenu.CommandIconIds.TryGetValue(Path.GetFileName(iconPath), out var id) ? commandDll + ",-" + id
                    : iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? iconPath + ",0" : iconPath;
                explorerCommandEnumerated = command.Title == ExplorerMenu.Title && (command.Flags & ExplorerCommandProbe.HasSubCommands) != 0 && command.State == ExplorerCommandProbe.Enabled
                    && command.Icon == markResource && Path.GetFileName(markIcon) == ExplorerMenu.MarkFileFor(ShellIcons.LightMenus())
                    && command.SubCommands.Select(item => item.Title).SequenceEqual(expectedTitles)
                    && command.SubCommands[0].Flags == ExplorerCommandProbe.SeparatorAfter && command.SubCommands[0].Icon == markResource && command.SubCommands.Skip(1).All(item => item.Flags == 0)
                    && command.SubCommands.Skip(1).Zip(menuItems).All(pair => pair.First.Icon == ExpectedIcon(pair.Second.IconPath))
                    // Each agent's item: the tool's exe where its icon is in one, otherwise SIC's letter tile, which the DLL answers from its resources.
                    && menuItems.Take(4).All(item => item.IconPath == AgentIcons.MenuIcon(IconSourceFor(item.Id), Path.Combine(exeDirectory, "Assets", "icons", item.Id + ".ico"), LibraryIconFor(item.Id)))
                    && command.SubCommands.Skip(1).Take(4).Zip(menuItems).All(pair => pair.Second.IconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || pair.Second.IconPath == LibraryIconFor(pair.Second.Id)
                        || pair.First.Icon!.StartsWith(commandDll + ",-10", StringComparison.OrdinalIgnoreCase))
                    && command.SubCommands.All(item => item.State == ExplorerCommandProbe.Enabled)
                    && hiddenWithoutMenu.State == ExplorerCommandProbe.Hidden && hiddenWithoutMenu.SubCommands.Count == 0;
                if (!explorerCommandEnumerated) throw new InvalidOperationException($"SicExplorerCommand.dll did not list the menu as laid out: title='{command.Title}' flags={command.Flags} state={command.State} icon='{command.Icon}' items=[{string.Join(", ", command.SubCommands.Select(item => $"{item.Title}|{item.Flags}|{item.Icon}"))}] hiddenState={hiddenWithoutMenu.State}.");
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(scratchMenuKey, throwOnMissingSubKey: false); }

            // Left for the restore stage, after everything measured with the rail on the left and the bars at the top: it expects the
            // rail on the right, the bars at the bottom and the widest pane border.
            SetSidebarSide(SidebarSide.Right);
            SetTabBarSide(TabBarSide.Bottom);
            SetPaneBorder(PaneBorder.Max);
            SetPaneBorderColor("green");
            // And a second folder, "kept" in the data folder, listed with one placeholder tab in it out of the strip, the first workspace still current.
            var keptFolder = Path.Combine(data, "kept");
            Directory.CreateDirectory(keptFolder);
            AddFolder(keptFolder);
            var keptHost = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "PowerShell · kept", [new SavedPane(Guid.NewGuid().ToString("N"), "shell", "PowerShell · kept", keptFolder, "")]), select: false);
            PlaceTab(keptHost, follow: false);
            SaveLayout();
            _options.WriteReport(new
            {
                success = true, terminalReady = true, inputEchoed = true, resized = true,
                welcomeSkipped, welcomeOpened, welcomeCard, welcomeImage, setupNotice, setupImage, tourStops, tourDropMark, tourLimits, tourEnded, tourImage,
                workspaceShell, brandMark, historySearch, previewShown, renamedSearch, placeholderRestored, reopenedNamed, closeToTray, minimizeToTray, closeAndMinimize,
                inlineRename, f2Rename, f2FromTerminal, tabColored, reopenedColored, tabStripLegible, activeTabJoinsPane, harnessNameDropped, panePathFits,
                sessionsGrouped, sectionsRuled, pinMarkDrawn, sessionsByFolder, newSessionFoldersListed, workspaceFolders, suggestedWorkspaces, usageLimits, statusLineKept, statusLineMs, limitsImage, contextWindow, folderEmoji, folderColors, sounds, folderExplorer, folderRowsPresented, foldersRelisted, workspaceFromFolder, updatesSection, appIconShipped, themeKeptScroll, userThemes, motionSetting, terminalOptionsApplied, cursorDrawn, copyOnSelect, terminalPaletteFixed,
                sidebarRight, captionWidth, closeButtonHit = (long)closeButtonHit, rightImage, rightLightImage, rightShutImage,
                tabBarBottom, tabsBottomImage, tabsBottomWindowImage, tabsBottomNoticeImage, tabsBottomLightImage, tabsBottomSplitImage, tabsBottomRightImage, fullScreen, fullScreenImage, fullScreenWindowImage, readyChime, chimesRung = ChimesRung, fontWidths,
                findMatched, dropPastesQuotedPath, pasteFilteredAndTrimmed, multiLinePasteAsked, pasteThroughHost, splitCreated, splitRefused, splitTiled, splitSaved, paneFocusMoved, paneRenamed, paneBorder, paneBorderImages, paneBorderPreview, tabDuplicated, tabMoved, workspacesGrouped, paneDragged, movedOut, crossMoved, tabsConsolidated, resumeJoined, failedStartRecovered, backgroundStartWaited, resumeAll, resumeAllButton, noticeToasted, f5ResumesAll, keyboardHelp, resumeAllOnLaunch, hiddenWindowStart, dragRegions, dragSettledMs, dragAreas = DragAreaText(_dragRects), presenceHeuristic, presenceTitle, presenceWorkingAfterMs = workingAfter, presenceQuietForMs = quietFor, activeTabColored, panelCollapsed, panelExpanded, panelSlid, panelResized, workspacesPanel,
                themesApplied, railAccentReadable, lightThemeApplied, darkThemeApplied, lightStartupApplied, gracefulExit, alwaysExitCleanly, quitDialog, windowPlacementTracked,
                openFolderReceived, openAgentStarted, openMissingRefused, identitySkipped, explorerCommandBuilt, explorerPackageSigned, explorerMenuOwned, explorerCommandEnumerated, panelKeptByResumeAll,
                consoleHost, consoleHostVersion = ConsoleHost.PackagedVersion, windowsPtyBuild = windowsPty.BuildNumber, zeroWidth, cursorRowsAgree, queryAnswersHidden,
                petImageDrawn, petImageCleared, petImageDeleted, petImagesOff, petImagesOffAtStart, petConsoleHost, consoleHostCurrent = ConsoleHost.Current,
                petInside, petOutside, petBox = new { left = petLeft, top = petTop, right = petRight, bottom = petBottom, scale = petScale }, petImage, petOffImage,
                hostRowAfterGrow = afterGrow.Row, referenceRowDrawn = referenceDrawn, shortRows,
                rendererLineHeight, originalColumns = original.Columns, resizedColumns = resized.Columns,
                nativeSizeReply = reply, terminalImage = image, windowTitle = Title,
                shellImage, lightShellImage, lightTerminalImage, compactShellImage, focusedShellImage, focusedTerminalImage, bannerImage, splitShellImage, stripFocusImage, keyboardHelpImage, foldersImage, themeImages, tabStripImages,
                detectedTools = _available,
                note = "Real native UI, SQLite fixture import/search, in-place preview, in-place tab and pane rename, tab colors, metadata-only restoration, split trees of six panes with Alt+Arrow between them, a tab dropped into another, a pane dragged within its tab, to the strip and into another tab, tabs consolidated by folder, find, every catalog theme, and a PowerShell/xterm.js/WebView2/ConPTY round-trip. Fixture histories only; no model request."
            });
        }
        catch (Exception exception)
        {
            _options.Trace("smoke-error " + exception.Message);
            Environment.ExitCode = 1;
            var terminal = _activePane?.Terminal;
            var screen = terminal is not null && terminal.Ready.IsCompletedSuccessfully ? await terminal.ReadScreenAsync() : "";
            var dimensions = terminal is not null && terminal.Ready.IsCompletedSuccessfully ? await terminal.GetDimensionsAsync() : default;
            _options.WriteReport(new { success = false, error = exception.ToString(), screen, columns = dimensions.Columns, rows = dimensions.Rows,
                rootWidth = _root.ActualWidth, terminalWidth = terminal?.ActualWidth, requestedWidth = terminal is { } current && double.IsFinite(current.Width) ? current.Width : (double?)null });
        }
        finally { await CloseAsync(force: true); }
    }

    /// <summary>
    /// Whether a tile shows what the last icon refresh found: the tool's own icon from the cache, at the frame for its size, on
    /// the tile colour its source names if it names one; or the agent's letter on its hue.
    /// </summary>
    private bool ShowsAgentIcon(Border? tile, string id, double size) => tile is not null && (_agentIconSources.GetValueOrDefault(id) is { } source
        ? (source.Plate is null ? tile.Background is null : tile.Background is Microsoft.UI.Xaml.Media.SolidColorBrush plate && Theme.Hex(plate.Color).TrimStart('#').Equals(source.Plate, StringComparison.OrdinalIgnoreCase))
            && tile.Child is Image { Source: BitmapImage image } && image.UriSource.LocalPath.EndsWith(id + "-" + (size <= 16 ? 32 : 48) + ".png", StringComparison.OrdinalIgnoreCase)
        : tile.Background is not null && tile.Child is TextBlock letter && letter.Text == AgentIcons.Letter(id));

    /// <summary>
    /// Title ink on an unselected tab (under the pointer, for a colored one) and on a selected tab, the pane-header ink on the
    /// selected fill, the strip's own ink and the ink of the tab's drag look all reach 4.5:1, and the title inherits the tab's
    /// ink rather than fixing its own.
    /// </summary>
    private bool TabInkReadable(TabHost host) =>
        Theme.Contrast(host.Paint.Ink.Color, Theme.Over(host.Paint.Bar.Visibility == Visibility.Visible ? host.Paint.Hover.Color : host.Paint.Idle.Color, _theme.StripSurface)) >= 4.5
        && Theme.Contrast(host.Paint.SelectedInk.Color, host.Paint.Selected.Color) >= 4.5
        && Theme.Contrast(host.Paint.HeaderInk.Color, host.Paint.Selected.Color) >= 4.5
        && Theme.Contrast(_theme.StripText.Color, _theme.StripSurface) >= 4.5
        && host.Title.ReadLocalValue(TextBlock.ForegroundProperty) == DependencyProperty.UnsetValue
        && TabDragFillReadable(host);

    /// <summary>
    /// The drag look a reorder gives the tab is filled with the tab's own opaque brush, not the Windows theme's, and the tab's
    /// ink reads on it: the selected ink on the active tab, the title ink on the others under the wash the pointer gives them.
    /// </summary>
    private bool TabDragFillReadable(TabHost host)
    {
        var selected = ReferenceEquals(_tabs.SelectedItem, host.Item);
        var under = selected ? host.Paint.Drag.Color : Theme.Over(host.Paint.Bar.Visibility == Visibility.Visible ? host.Paint.Hover.Color : host.Paint.Idle.Color, host.Paint.Drag.Color);
        return FindNamed<Grid>(host.Item, "TabDragVisualContainer") is { Background: Microsoft.UI.Xaml.Media.SolidColorBrush { Color: var fill } } && fill == host.Paint.Drag.Color
            && host.Paint.Drag.Color.A == 255 && Theme.Contrast(selected ? host.Paint.SelectedInk.Color : host.Paint.Ink.Color, under) >= 4.5;
    }

    /// <summary>Whether the tab's template shows the drag look a reorder gives it.</summary>
    private static bool InDragLook(TabViewItem item) =>
        Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(item) > 0 && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(item, 0) is FrameworkElement root
        && VisualStateManager.GetVisualStateGroups(root).Any(group => group.CurrentState?.Name == "DragDropVisualVisible");

    /// <summary>
    /// A colored tab carries its color while selected (the active wash over the pane-header surface) and only its stripe while
    /// not; an uncolored tab keeps the plain surface. Either way its pane headers are filled with the tab's own selected brush.
    /// </summary>
    private bool TabColorFollowsSelection(TabHost host)
    {
        var colored = TabColors.Resolve(EffectiveColor(host.Saved), _theme.StripDark) is not null;
        var expected = colored ? Theme.Over(Theme.WithAlpha(ActiveShade(host.Saved), ActiveWash), _theme.SelectedTab.Color) : _theme.SelectedTab.Color;
        return host.Panes.All(pane => ReferenceEquals(pane.HeaderFill, host.Paint.Selected)) && host.Paint.Selected.Color == expected
            && host.Paint.Idle.Color.A == 0 && colored == (host.Paint.Selected.Color != _theme.SelectedTab.Color);
    }

    /// <summary>The last line of the tab's tooltip: the state word.</summary>
    private static string TabStateWord(TabHost host) => ToolTipService.GetToolTip(host.Item) is string tip ? tip[(tip.LastIndexOf('\n') + 1)..] : "";

    /// <summary>A working tab shows the spinning ring in place of its dot.</summary>
    private static bool SpinnerShown(TabHost host) => host.Ring is { Visibility: Visibility.Visible, IsActive: true } && host.Dot.Visibility == Visibility.Collapsed;

    /// <summary>
    /// The tab's canvas holds exactly its panes and one divider per split, the panes are listed in the tree's order, and, once
    /// the canvas is laid out (a tab is laid out only while it is showing), every pane and divider sits where
    /// <see cref="PaneLayout.Arrange"/> puts it for the canvas's size, to within layout rounding.
    /// </summary>
    private static bool PanesTiled(TabHost host)
    {
        var canvas = host.Content;
        var tree = host.Saved.Layout ?? throw new InvalidOperationException("The tab has no tree.");
        var splits = PaneLayout.Splits(tree);
        // The canvas's children are its panes, one divider per split, and the drop mark.
        var structure = canvas.Tree == tree && canvas.Children.Count(child => child is SessionPane or PaneSplitter) == host.Panes.Count + splits.Count && host.Panes.All(canvas.Children.Contains)
            && splits.All(split => canvas.Dividers.ContainsKey(split.Path)) && host.Panes.Select(pane => pane.Descriptor.Id).SequenceEqual(PaneLayout.Leaves(tree));
        if (!structure) return false;
        if (canvas.XamlRoot is null) return true;
        canvas.UpdateLayout();
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return true;
        var expected = PaneLayout.Arrange(tree, canvas.ActualWidth, canvas.ActualHeight, PaneSplitter.GrabSize);
        bool At(FrameworkElement element, PaneRect rect)
        {
            var origin = element.TransformToVisual(canvas).TransformPoint(new Windows.Foundation.Point(0, 0));
            return Math.Abs(origin.X - rect.X) < 1.5 && Math.Abs(origin.Y - rect.Y) < 1.5 && Math.Abs(element.ActualWidth - rect.Width) < 1.5 && Math.Abs(element.ActualHeight - rect.Height) < 1.5;
        }
        return expected.Panes.All(placement => At(host.Panes.Single(pane => pane.Descriptor.Id == placement.Pane), placement.Rect))
            && expected.Dividers.All(divider => At(canvas.Dividers[divider.Path], divider.Rect));
    }

    /// <summary>A PNG's pixels as BGRA rows, for checks that read colours off a capture.</summary>
    private static async Task<(int Width, int Height, byte[] Bgra)> ReadPixelsAsync(string path)
    {
        await using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            new Windows.Graphics.Imaging.BitmapTransform(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
    }

    /// <summary>The colour (#rrggbb) of a capture at a point given in CSS pixels, <paramref name="scale"/> capture pixels to one.</summary>
    private static string ColorAt((int Width, int Height, byte[] Bgra) image, double scale, double x, double y)
    {
        var column = Math.Clamp((int)Math.Floor(x * scale), 0, image.Width - 1);
        var row = Math.Clamp((int)Math.Floor(y * scale), 0, image.Height - 1);
        var index = (row * image.Width + column) * 4;
        return $"#{image.Bgra[index + 2]:x2}{image.Bgra[index + 1]:x2}{image.Bgra[index]:x2}";
    }

    /// <summary>Two #rrggbb colours within a few levels of each other in every channel.</summary>
    private static bool ColorsNear(string first, string second, int tolerance = 6) =>
        first.Length == 7 && second.Length == 7 && Enumerable.Range(0, 3).All(channel =>
            Math.Abs(Convert.ToInt32(first.Substring(1 + channel * 2, 2), 16) - Convert.ToInt32(second.Substring(1 + channel * 2, 2), 16)) <= tolerance);

    private static string DragAreaText(IEnumerable<PixelRect> rects) => string.Join(" ", rects.Select(rect => $"[{rect.X},{rect.Y} {rect.Width}x{rect.Height}]"));

    /// <summary>Whether a condition comes true within 3 s, for a check that reports rather than throws.</summary>
    /// <summary>Every TextBlock's text under <paramref name="element"/>, through panels, borders, viewboxes and content controls.</summary>
    private static List<string> TextsIn(DependencyObject element)
    {
        var texts = new List<string>();
        void Walk(object? node)
        {
            switch (node)
            {
                case TextBlock text: texts.Add(text.Text); break;
                case Panel panel: foreach (var child in panel.Children) Walk(child); break;
                case Border border: Walk(border.Child); break;
                case Viewbox box: Walk(box.Child); break;
                case ContentControl control: Walk(control.Content); break;
            }
        }
        Walk(element);
        return texts;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)] private static extern nint GetWindowLongPtr(nint window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "OpenClipboard", SetLastError = true)] [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool SmokeOpenClipboard(nint owner);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CloseClipboard")] [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool SmokeCloseClipboard();

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var waited = 0; waited < 3000; waited += 75) { if (condition()) return true; await Task.Delay(75); }
        return condition();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await condition()) await Task.Delay(75, timeout.Token);
    }

    /// <summary>
    /// Waits as <see cref="WaitForAsync"/> does for the terminal's size to meet <paramref name="condition"/>, but returns the last
    /// size read instead of throwing, so a check that runs out of time can say what size it saw.
    /// </summary>
    private static async Task<(int Columns, int Rows)> WaitForSizeAsync(TerminalView terminal, Func<(int Columns, int Rows), bool> condition)
    {
        var size = await terminal.GetDimensionsAsync();
        for (var waited = System.Diagnostics.Stopwatch.StartNew(); !condition(size) && waited.Elapsed < TimeSpan.FromSeconds(15); size = await terminal.GetDimensionsAsync())
            await Task.Delay(75);
        return size;
    }
}
