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

/// <summary>Naming tabs and panes: the tab's own name, the pane's, and the conversation's local name they become.</summary>
internal sealed partial class MainWindow
{
    /// <summary>
    /// Names the tab. A tab of one pane is that pane, so the name is the pane's too (the conversation's local name, which
    /// Sessions shows); a split tab's name is its own and its panes keep theirs.
    /// </summary>
    private void SetCustomTabTitle(TabViewItem tab, string title)
    {
        if (!_open.TryGetValue(tab, out var host)) throw new InvalidOperationException("The tab is no longer open.");
        title = title.Trim();
        if (!TabLabels.IsValidName(title)) throw new InvalidDataException("A name must be 1–120 characters on one line.");
        host.Saved = host.Saved with { CustomTitle = title };
        tab.Tag = host.Saved;
        if (host.Panes.Count == 1) NamePane(host.Panes[0], title);
        RefreshTabStatus(host);
        SavePreferences();
        SaveLayout();
    }

    /// <summary>
    /// Names a pane, from its in-place editor or its menu. The name is kept as the conversation's local name when the pane is
    /// linked to one, so Sessions shows it and it comes back when the conversation is reopened, and on the pane itself until
    /// then. A tab without a name of its own follows its first pane, so it may change here too.
    /// </summary>
    private async Task RenamePaneAsync(SessionPane pane, string name)
    {
        try
        {
            if (HostOf(pane) is not { } host) throw new InvalidOperationException("The pane is no longer open.");
            name = name.Trim();
            if (!TabLabels.IsValidName(name)) throw new InvalidDataException("A name must be 1–120 characters on one line.");
            NamePane(pane, name);
            RefreshTabStatus(host);
            SavePreferences();
            SaveLayout();
            await SearchAsync();
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    /// <summary>Sets the pane's name where it is kept and shows it; the callers save.</summary>
    private void NamePane(SessionPane pane, string name)
    {
        var descriptor = pane.Descriptor;
        if (descriptor.SessionId is { } id) _preferences.ConversationNames[TabLabels.Key(descriptor.AgentId, id)] = name;
        else pane.UpdateIdentity(descriptor with { CustomTitle = name });
        pane.RefreshName();
        if (pane.Terminal is null) { if (descriptor.SessionId is { } session) ShowSavedPreview(pane, TabLabels.Key(descriptor.AgentId, session)); else pane.ShowPreview(null); }
        if (HostOf(pane) is { } host) { host.Saved = host.Saved with { Panes = host.Panes.Select(item => item.Descriptor).ToList() }; host.Item.Tag = host.Saved; }
    }
}
