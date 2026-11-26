using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// A Settings drop list over a catalog of things with ids (the themes, the sounds): the list, its items by id, whether a
/// selection is being made by code rather than by the user (a change handler ignores those), and the line under the list
/// naming the files that could not be used.
/// </summary>
internal sealed class CatalogPicker
{
    private readonly Dictionary<string, ComboBoxItem> _items;

    public ComboBox Box { get; }
    public TextBlock Problems { get; }
    public IReadOnlyDictionary<string, ComboBoxItem> Items => _items;
    public bool Syncing { get; private set; }

    public CatalogPicker(Theme theme, string header, StringComparer comparer)
    {
        _items = new Dictionary<string, ComboBoxItem>(comparer);
        Box = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
        Problems = theme.Tertiary("");
        Problems.Foreground = theme.Warning; Problems.TextWrapping = TextWrapping.Wrap; Problems.TextTrimming = TextTrimming.None; Problems.Visibility = Visibility.Collapsed;
    }

    /// <summary>The id of the item selected, or null while none is.</summary>
    public string? SelectedId => Box.SelectedItem is ComboBoxItem { Tag: string id } ? id : null;

    /// <summary>Lists the catalog as it is now, in the order given, with a tooltip on the entries that have one.</summary>
    public void Fill(IEnumerable<(string Id, string Label, string? Tip)> entries)
    {
        Syncing = true;
        try
        {
            _items.Clear();
            Box.Items.Clear();
            foreach (var (id, label, tip) in entries)
            {
                var item = new ComboBoxItem { Content = label, Tag = id };
                if (tip is not null) ToolTipService.SetToolTip(item, tip);
                _items[id] = item;
                Box.Items.Add(item);
            }
        }
        finally { Syncing = false; }
    }

    /// <summary>Puts the list on the entry with this id, or on none, without it counting as the user's choice.</summary>
    public void Select(string? id)
    {
        Syncing = true;
        try { Box.SelectedItem = id is null ? null : _items.GetValueOrDefault(id); }
        finally { Syncing = false; }
    }

    /// <summary>The files that could not be used, each with its error; the line is hidden while every file reads.</summary>
    public void ShowProblems(IReadOnlyCollection<(string File, string Error)> problems)
    {
        Problems.Text = string.Join("\n", problems.Select(problem => problem.File + ": " + problem.Error));
        Problems.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
