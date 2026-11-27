using Sic.App.Controls;
using Sic.Core.Agents;
using Sic.Core.Alerts;
using Sic.Core.Appearance;
using Sic.Core.Sessions;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Path = System.IO.Path;

namespace Sic.App;

/// <summary>The keyboard shortcuts routed by name, and the tab offset they move by.</summary>
internal sealed partial class MainWindow
{
    private async Task HandleShortcutAsync(string name)
    {
        try
        {
            switch (name)
            {
                case "search": FocusSearch(); break;
                case "new-tab": await ShowNewSessionAsync(); break;
                case "close-tab": if (_tabs.SelectedItem is TabViewItem closing) await CloseTabAsync(closing); break;
                case "next-tab": SelectTabOffset(1); break;
                case "previous-tab": SelectTabOffset(-1); break;
                case "find": _activePane?.OpenFind(); break;
                case "open-folder": OpenChosenFolder(); break;
                case "split": await SplitAsync(SelectedHost, SelectedHost is { } splitting ? FocusedPane(splitting) : null, PaneSplit.Columns); break;
                case "split-down": await SplitAsync(SelectedHost, SelectedHost is { } stacking ? FocusedPane(stacking) : null, PaneSplit.Rows); break;
                case "duplicate-tab": if (SelectedHost is { } duplicating) await DuplicateAsync(FocusedPane(duplicating)); break;
                case "rename": RenamePaneOrTab(); break;
                case "resume-all": await ResumeAllFromUserAsync(new("F5 or Ctrl+Shift+R", DateTimeOffset.UtcNow)); break;
                case "resume-workspace": await ResumeWorkspaceFromUserAsync(_workspace, new("Ctrl+F5", DateTimeOffset.UtcNow)); break;
                case "consolidate": ConsolidateTabsFromUser(); break;
                case "help": await ShowKeyboardHelpAsync(); break;
                case "toggle-panel": await SetPanelOpen(!_panelOpen); break;
                case "full-screen": SetFullScreen(!_fullScreen); break;
                case "rename-tab": if (SelectedHost is { } renaming) BeginRename(renaming); break;
                case "next-workspace": StepWorkspace(1); break;
                case "previous-workspace": StepWorkspace(-1); break;
                case "workspace-list": ShowWorkspaceSwitcher(); break;
                // From a terminal's page, which only sends these toward a pane.
                case "focus-left": MovePaneFocus(PaneDirection.Left); break;
                case "focus-right": MovePaneFocus(PaneDirection.Right); break;
                case "focus-up": MovePaneFocus(PaneDirection.Up); break;
                case "focus-down": MovePaneFocus(PaneDirection.Down); break;
                default:
                    if (name.StartsWith("tab-", StringComparison.Ordinal) && int.TryParse(name[4..], out var digit) && _tabs.TabItems.Count > 0)
                        _tabs.SelectedIndex = digit >= 9 ? _tabs.TabItems.Count - 1 : Math.Min(digit - 1, _tabs.TabItems.Count - 1);
                    else if (name.StartsWith("workspace-", StringComparison.Ordinal) && int.TryParse(name[10..], out var place)) SelectWorkspaceByDigit(place);
                    break;
            }
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    /// <summary>F2: the focused pane of a split tab, or the tab of one pane, whose name is its pane's too. Ctrl+F2 renames the tab whatever it holds.</summary>
    private void RenamePaneOrTab()
    {
        if (SelectedHost is not { } host) return;
        if (host.Panes.Count > 1) FocusedPane(host).BeginRename();
        else BeginRename(host);
    }

    /// <summary>
    /// F2 pressed outside a terminal: on the tab strip it renames the tab with the keyboard focus, and anywhere else it does what
    /// it does from a terminal. A terminal's own F2 never asks where the window's focus is, since it may lag the key.
    /// </summary>
    private void RenameFocused()
    {
        if (FocusManager.GetFocusedElement(_root.XamlRoot) is TabViewItem focused && _open.TryGetValue(focused, out var onStrip)) BeginRename(onStrip);
        else RenamePaneOrTab();
    }

    private void SelectTabOffset(int offset)
    {
        var count = _tabs.TabItems.Count;
        if (count == 0) return;
        _tabs.SelectedIndex = ((_tabs.SelectedIndex + offset) % count + count) % count;
    }
}
