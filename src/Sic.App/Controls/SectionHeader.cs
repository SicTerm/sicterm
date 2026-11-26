using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Sic.App.Controls;

/// <summary>
/// A section's header in the side panel, the same in the session list and the Workspaces panel (docs/decisions/sessions-groups.md):
/// a chevron that says whether the section is folded down to this line, the label in the eyebrow style, the count of what is
/// in the section, a note at the right (when the section was last written), and a slot at the end for the section's own
/// buttons. A folder section may carry its emoji before the label and wear its color as the label's ink (a swatch after the chevron until November 12, 2026). The owner handles the tap
/// that folds the section; the header only shows the state.
/// </summary>
internal sealed class SectionHeader : Grid
{
    private const string FoldedGlyph = "", UnfoldedGlyph = "";
    private readonly FontIcon _chevron;
    private readonly TextBlock _mark, _count, _note;
    private readonly Theme _theme;
    private bool _folded, _canFold = true;

    /// <summary>The label, for the display font's register.</summary>
    internal TextBlock Label { get; }
    /// <summary>Buttons at the end of the line, the Workspaces section's Pin common folders and Add folder.</summary>
    internal StackPanel Trailing { get; }

    internal SectionHeader(Theme theme, string label)
    {
        _theme = theme;
        ColumnSpacing = 6;
        MinHeight = 24;
        Background = theme.Transparent;
        for (var column = 0; column < 7; column++) ColumnDefinitions.Add(new ColumnDefinition { Width = column == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        _chevron = Theme.Icon(UnfoldedGlyph, 9, theme.TextSecondary);
        _chevron.Width = 12; _chevron.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_chevron);
        _mark = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        SetColumn(_mark, 2); Children.Add(_mark);
        Label = theme.Eyebrow(label);
        Label.FontSize = 12; Label.Foreground = theme.Text; Label.TextTrimming = TextTrimming.CharacterEllipsis;
        SetColumn(Label, 3); Children.Add(Label);
        _count = theme.Tertiary("");
        SetColumn(_count, 4); Children.Add(_count);
        _note = theme.Tertiary("");
        _note.HorizontalAlignment = HorizontalAlignment.Right; _note.Margin = new Thickness(8, 0, 0, 0);
        SetColumn(_note, 5); Children.Add(_note);
        Trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        SetColumn(Trailing, 6); Children.Add(Trailing);
        AutomationProperties.SetHeadingLevel(Label, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        Describe();
    }

    /// <summary>Whether the section is folded down to this header; the chevron points right while it is.</summary>
    internal bool Folded
    {
        get => _folded;
        set { _folded = value; _chevron.Glyph = value && _canFold ? FoldedGlyph : UnfoldedGlyph; Describe(); }
    }

    /// <summary>Whether the section can fold at all: an empty Workspaces section cannot, so its hint stays in view, and shows no chevron.</summary>
    internal bool CanFold
    {
        get => _canFold;
        set { _canFold = value; _chevron.Visibility = value ? Visibility.Visible : Visibility.Collapsed; Folded = _folded; }
    }

    /// <summary>The count after the label; nothing for zero.</summary>
    internal int Count { set => _count.Text = value > 0 ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""; }

    /// <summary>The note at the right end: when the section was last written, say.</summary>
    internal string Note { set => _note.Text = value; }

    /// <summary>The folder's emoji before the label, or none.</summary>
    internal string? Mark { set { _mark.Text = value ?? ""; _mark.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible; } }

    /// <summary>The label's ink: a folder's color, as its rows wear it in the Folders and Agent Workspaces panels, or null for the text's own.</summary>
    internal Brush? Ink { set => Label.Foreground = value ?? _theme.Text; }

    private void Describe()
    {
        AutomationProperties.SetName(this, Label.Text + (_canFold ? _folded ? ", folded" : ", unfolded" : ""));
        ToolTipService.SetToolTip(_chevron, _canFold ? _folded ? "Unfold" : "Fold" : null);
    }
}
