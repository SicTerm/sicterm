using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Sic.App;

/// <summary>
/// The in-place name editor a tab's title and a pane's name share: a text box exactly the height of the row it stands in for,
/// focused with its text selected as soon as it is in the tree. Enter saves and hands focus back to the terminal, Esc cancels and
/// does the same, clicking away saves and leaves focus where the click put it; the caller's <paramref name="end"/> does the
/// saving and the focus (commit, refocus) and takes the box out. The agent's own history is never renamed.
/// </summary>
internal static class InlineRename
{
    internal static TextBox Editor(Theme theme, string text, double height, double fontSize, double maxWidth, string automationName, Func<bool, bool, Task> end)
    {
        var editor = new TextBox
        {
            Text = text, MaxLength = 120, MinWidth = 160, MaxWidth = maxWidth, MinHeight = 0, Height = height, FontSize = fontSize, FontFamily = Theme.Body,
            Padding = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        };
        theme.Accentuate(editor.Resources);
        ToolTipService.SetToolTip(editor, "Enter saves, Esc cancels. The agent's own history is not renamed.");
        AutomationProperties.SetName(editor, automationName);
        editor.KeyDown += async (_, args) =>
        {
            if (args.Key is not (VirtualKey.Enter or VirtualKey.Escape)) return;
            args.Handled = true;
            await end(args.Key == VirtualKey.Enter, true);
        };
        editor.LostFocus += async (_, _) => await end(true, false);
        editor.Loaded += (_, _) => { editor.Focus(FocusState.Programmatic); editor.SelectAll(); };
        return editor;
    }
}
