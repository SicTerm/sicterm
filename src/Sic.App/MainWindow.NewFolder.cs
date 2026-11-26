using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sic.Core.State;

namespace Sic.App;

/// <summary>
/// New folder, in the menu of every folder of the Folders panel, listed or in a tree (Chris, November 12, 2026): a dialog asks
/// for the name, the folder is made in the one right-clicked, and that one opens to show it.
/// </summary>
internal sealed partial class MainWindow
{
    private MenuFlyoutItem NewFolderItem(string parent)
    {
        var item = new MenuFlyoutItem { Text = "New folder…", Icon = new FontIcon { Glyph = NewFolderGlyph, FontFamily = Theme.Icons } };
        item.Click += async (_, _) => await ShowNewFolderAsync(parent);
        return item;
    }

    /// <summary>
    /// Asks for the new folder's name, starting from "New folder" or the first "New folder (n)" that is free. Create is off while
    /// the name is one Windows refuses, with the reason under the box; whether the name is taken is found out as the folder is made.
    /// </summary>
    private async Task ShowNewFolderAsync(string parent)
    {
        string offered;
        try
        {
            var found = await Task.Run(() => Directory.Exists(parent) ? FolderNames.Unused(FolderNames.Default, name => Path.Exists(Path.Combine(parent, name))) : null);
            if (found is null) { ShowError("That folder no longer exists: " + parent); return; }
            offered = found;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ShowError(exception.Message); return; }
        var box = new TextBox { Header = "Name", Text = offered, MaxLength = FolderNames.MaxLength };
        var problem = _theme.Caption("");
        problem.TextWrapping = TextWrapping.Wrap;
        var place = _theme.Caption("In " + parent);
        ToolTipService.SetToolTip(place, parent);
        var stack = new StackPanel { Spacing = 8, MinWidth = 380 };
        stack.Children.Add(place);
        stack.Children.Add(box);
        stack.Children.Add(problem);
        var dialog = Dialog("New folder", stack, "Create");
        void Check()
        {
            var reason = FolderNames.Problem(box.Text);
            // The reason for an empty box is plain to see; the button being off says enough.
            problem.Text = reason is null || FolderNames.Clean(box.Text).Length == 0 ? "" : reason;
            dialog.IsPrimaryButtonEnabled = reason is null;
        }
        box.TextChanged += (_, _) => Check();
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        Check();
        ContentDialogResult result;
        // Another dialog is already up.
        try { result = await dialog.ShowAsync(); }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException) { return; }
        if (result == ContentDialogResult.Primary) await CreateFolderAsync(parent, box.Text);
    }

    /// <summary>
    /// Makes a folder in another, off the UI thread since a network or WSL folder can take seconds, and shows it: the folder it
    /// was made in is listed again and opened, wherever the Folders panel has it. Returns the new folder's path, or null with the
    /// reason shown when it could not be made.
    /// </summary>
    internal async Task<string?> CreateFolderAsync(string parent, string name)
    {
        name = FolderNames.Clean(name);
        if (FolderNames.Problem(name) is { } problem) { ShowError(problem); return null; }
        var path = Path.Combine(parent, name);
        var error = await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(parent)) return "That folder no longer exists: " + parent;
                if (Path.Exists(path)) return "\"" + name + "\" is already in " + parent + ".";
                Directory.CreateDirectory(path);
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return exception.Message; }
        });
        if (error is not null) { ShowError(error); return null; }
        if (_closing) return path;
        // A listed folder's row opens its tree; a folder in a tree opens its row. One folder can be both, in two trees.
        if (_folderRows.TryGetValue(PinnedFolders.Normalize(parent), out var view))
        {
            if (FolderFolded(parent)) ToggleFolderFold(parent);
            else await RelistRootAsync(view, quiet: true);
        }
        foreach (var node in _folderPaths.Where(entry => PinnedFolders.Same(entry.Value, parent)).Select(entry => entry.Key).ToArray())
        {
            await RelistFolderAsync(node, quiet: true);
            if (_folderPaths.ContainsKey(node)) node.IsExpanded = true;
        }
        return path;
    }
}
