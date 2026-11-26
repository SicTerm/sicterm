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

/// <summary>Which tab and pane have the keyboard: tab selection, the active pane, and Alt+Arrow between panes and the strip.</summary>
internal sealed partial class MainWindow
{
    /// <summary>The tab painted as the selected one, so a selection repaints it and the new one rather than every tab (<see cref="PaintTab"/> reads the selection for two of its inks).</summary>
    private TabHost? _paintedSelected;

    private void OnTabSelected()
    {
        // The strip's items are being swapped for another workspace's (SwitchWorkspace): the selection settles once they are.
        if (_switchingWorkspace) return;
        var host = SelectedHost;
        RefreshInView();
        var previous = _paintedSelected;
        _paintedSelected = host;
        if (previous is not null && previous != host && _open.ContainsKey(previous.Item)) PaintTab(previous);
        if (host is not null) PaintTab(host);
        var pane = host is { Panes.Count: > 0 } ? FocusedPane(host) : null;
        SetActivePane(pane);
        // A tab chosen on the tab strip from the keyboard keeps the keyboard there, for the next Alt+Left or Alt+Right; a click,
        // a shortcut or a new tab gives it to the pane.
        var onStrip = _root.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is TabViewItem { FocusState: FocusState.Keyboard } focused && ReferenceEquals(focused, host?.Item);
        if (pane is { IsRunning: true } && host?.Editor is null && !onStrip) pane.FocusInput();
        if (_started && !_closing) SaveLayout();
    }

    // Counts activations, so each pane's LastActive says which of two panes was focused later.
    private long _activations;

    private void SetActivePane(SessionPane? pane)
    {
        if (_activePane == pane) return;
        _activePane?.SetActive(false);
        _activePane?.CancelFocus();
        _activePane = pane;
        if (pane is not null) pane.LastActive = ++_activations;
        RefreshActiveRing();
        // The Workspaces panel lights the row of the pane with the keyboard.
        if (!_restoring) RefreshWorkspaceStates();
    }

    // Set while Settings → Session Indicator Width's slider has the keyboard.
    private bool _paneBorderPreview;

    /// <summary>
    /// The slider of Settings → Session Indicator Width taking the keyboard, or letting it go: while it has it, the active pane
    /// wears its border even alone in its tab, so the width shows as the slider moves.
    /// </summary>
    private void PreviewPaneBorder(bool on)
    {
        _paneBorderPreview = on;
        RefreshActiveRing();
    }

    /// <summary>Draws the active pane's border in a tab of two or more panes, or in any tab while the width is being set.</summary>
    private void RefreshActiveRing()
    {
        if (_activePane is not { } pane) return;
        var host = _open.Values.FirstOrDefault(item => item.Panes.Contains(pane));
        pane.SetActive(host is not null && (host.Panes.Count > 1 || _paneBorderPreview));
    }

    /// <summary>The side of the panes the tab strip lies on: above the top row, or below the bottom row with the tabs at the bottom (Settings → Tab bar).</summary>
    private PaneDirection StripSide => TabsBottom ? PaneDirection.Down : PaneDirection.Up;

    /// <summary>
    /// Alt+Arrow: moves the keyboard focus from the selected tab's focused pane to the pane on that side
    /// (<see cref="PaneLayout.Neighbor"/>), or from the row beside the strip on to the tab strip (<see cref="StripSide"/>). False
    /// when there is nothing that way, so the key goes on to where it was pressed, as Windows Terminal passes it on.
    /// </summary>
    private bool MovePaneFocus(PaneDirection direction)
    {
        if (SelectedHost is not { Panes.Count: > 0 } host) return false;
        var panes = host.Panes.ToDictionary(pane => pane.Descriptor.Id, StringComparer.Ordinal);
        var next = PaneLayout.Neighbor(host.Content.Tree, FocusedPane(host).Descriptor.Id, direction, id => panes.TryGetValue(id, out var pane) ? pane.LastActive : 0);
        if (next is not null && panes.TryGetValue(next, out var target))
        {
            SetActivePane(target);
            target.FocusInput();
            return true;
        }
        // Above the top row (or below the bottom row) is the tab strip, where Alt+Left and Alt+Right move between tabs and the
        // opposite Alt+Arrow comes back in.
        return direction == StripSide && host.Item.Focus(FocusState.Keyboard);
    }

    /// <summary>
    /// The window's Alt+Arrow keys, pressed anywhere but in a terminal: a terminal decides in its page, the only place that can
    /// still pass the key to the program. On the tab strip they move between tabs; anywhere else, between the selected tab's panes.
    /// </summary>
    private bool MoveFocus(PaneDirection direction) => FocusManager.GetFocusedElement(_root.XamlRoot) switch
    {
        WebView2 => false,
        TabViewItem tab => MoveFromTab(tab, direction),
        _ => MovePaneFocus(direction)
    };

    /// <summary>
    /// Alt+Arrow on the tab strip: Left and Right show the tab beside the focused one and keep the keyboard on the strip, and
    /// stop at its ends; towards the panes (Down, or Up with the tabs at the bottom) goes into the focused tab (<see cref="EnterTab"/>).
    /// </summary>
    private bool MoveFromTab(TabViewItem tab, PaneDirection direction)
    {
        if (direction == (TabsBottom ? PaneDirection.Up : PaneDirection.Down)) return EnterTab(tab);
        var index = _tabs.TabItems.IndexOf(tab);
        var next = index + (direction == PaneDirection.Left ? -1 : 1);
        if (direction == StripSide || index < 0 || next < 0 || next >= _tabs.TabItems.Count || _tabs.TabItems[next] is not TabViewItem beside) return false;
        // Focused before it is selected, so OnTabSelected leaves the keyboard on the strip.
        beside.Focus(FocusState.Keyboard);
        _tabs.SelectedItem = beside;
        return true;
    }

    /// <summary>Shows the tab and gives the keyboard to the pane focused last in it: Down, Alt+Down or Enter on the tab strip (Up and Alt+Up with the tabs at the bottom), and Esc for the tab showing.</summary>
    private bool EnterTab(TabViewItem tab)
    {
        if (!_open.TryGetValue(tab, out var host)) return false;
        _tabs.SelectedItem = tab;
        var pane = FocusedPane(host);
        SetActivePane(pane);
        pane.FocusInput();
        return true;
    }

    private static bool AltHeld => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
}
