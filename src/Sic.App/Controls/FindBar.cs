using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Sic.App.Controls;

/// <summary>The in-terminal search strip: query, match position, previous/next, close. Search itself runs in the renderer.</summary>
internal sealed class FindBar : Grid
{
    private readonly TextBox _query = new() { PlaceholderText = "Find in terminal", MinWidth = 220, MaxLength = 512 };
    private readonly TextBlock _count;
    // A search as typed runs once the typing has paused: each one scans the whole scrollback and marks every match, so a word
    // typed as five keystrokes used to be five scans, each across to the page and back.
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _typing;
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(100);

    internal event Action<string, bool, bool>? FindRequested;
    internal event Action? Closed;
    internal string Query => _query.Text;

    internal FindBar(Theme theme)
    {
        _typing = DispatcherQueue.CreateTimer();
        _typing.Interval = TypingPause;
        _typing.IsRepeating = false;
        _typing.Tick += (_, _) => FindRequested?.Invoke(_query.Text, false, true);
        Visibility = Visibility.Collapsed;
        Background = theme.Layer;
        BorderBrush = theme.Stroke;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(8, 6, 8, 6);
        ColumnSpacing = 6;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 420 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = Theme.Icon("", 14, theme.TextSecondary);
        glyph.Margin = new Thickness(4, 0, 2, 0);
        Children.Add(glyph);
        _query.TextChanged += (_, _) => { _typing.Stop(); _typing.Start(); };
        _query.KeyDown += (_, args) =>
        {
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (args.Key == Windows.System.VirtualKey.Enter) { FlushTyping(); FindRequested?.Invoke(_query.Text, shift, false); args.Handled = true; }
            else if (args.Key == Windows.System.VirtualKey.Escape) { Close(); args.Handled = true; }
        };
        SetColumn(_query, 1); Children.Add(_query);
        _count = theme.Tertiary("");
        _count.MinWidth = 56; _count.Margin = new Thickness(6, 0, 2, 0);
        SetColumn(_count, 2); Children.Add(_count);
        var previous = theme.IconButton("", "Previous match (Shift+Enter)", 28, 12);
        previous.Click += (_, _) => { FlushTyping(); FindRequested?.Invoke(_query.Text, true, false); };
        SetColumn(previous, 3); Children.Add(previous);
        var next = theme.IconButton("", "Next match (Enter)", 28, 12);
        next.Click += (_, _) => { FlushTyping(); FindRequested?.Invoke(_query.Text, false, false); };
        SetColumn(next, 4); Children.Add(next);
        var close = theme.IconButton("", "Close (Esc)", 28, 12);
        close.Click += (_, _) => Close();
        SetColumn(close, 6); Children.Add(close);
    }

    /// <summary>Runs a search as typed that still waits for the typing to pause, so Enter and the buttons step on from its match as they did when every keystroke searched.</summary>
    private void FlushTyping()
    {
        if (!_typing.IsRunning) return;
        _typing.Stop();
        FindRequested?.Invoke(_query.Text, false, true);
    }

    internal void Open()
    {
        Visibility = Visibility.Visible;
        _query.Focus(FocusState.Programmatic);
        _query.SelectAll();
    }

    internal void Close()
    {
        if (Visibility == Visibility.Collapsed) return;
        _typing.Stop();
        Visibility = Visibility.Collapsed;
        _count.Text = "";
        Closed?.Invoke();
    }

    internal bool IsOpen => Visibility == Visibility.Visible;

    internal void SetResults(int index, int count) =>
        _count.Text = _query.Text.Length == 0 ? "" : count == 0 ? "No matches" : index < 0 ? count + " matches" : (index + 1) + " of " + count;
}
