using Sic.Core.State;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;

namespace Sic.App.Controls;

/// <summary>
/// A 1 px divider with an 8 px grab area that resizes what lies on one side of it: it sets a size in pixels, which the pane
/// canvas turns into its split's ratio and the panel's edge into the panel's width. Wraps a Thumb because WinUI seals it.
/// </summary>
internal sealed class PaneSplitter : Grid
{
    private const string TemplateXaml =
        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Thumb\">" +
        "<Grid Background=\"Transparent\"><Rectangle {0} Fill=\"{{TemplateBinding Foreground}}\" /></Grid></ControlTemplate>";
    internal const double GrabSize = 8;
    private readonly Thumb _thumb = new();

    /// <summary>Once a drag ends, with the size it left behind already applied.</summary>
    internal event Action? Released;
    /// <summary>
    /// True when what the divider resizes lies after it (to its right, or below), so a drag that way shrinks it: the panel's
    /// edge while the rail is on the right, where the panel lies between the edge and the rail.
    /// </summary>
    internal bool Reversed { get; set; }
    /// <summary>Whether a drag is under way.</summary>
    internal bool IsDragging => _thumb.IsDragging;

    /// <summary>
    /// A divider that sets a size in pixels: <paramref name="size"/> reads the current size of what it resizes, and
    /// <paramref name="resize"/> is handed the size each drag step asks for and clamps it as its owner requires. Without a
    /// <paramref name="line"/> the grab area is invisible, for an edge that already draws its own stroke.
    /// </summary>
    internal PaneSplitter(bool rows, Func<double> size, Action<double> resize, Theme theme, bool line = true)
    {
        if (rows) Height = GrabSize; else Width = GrabSize;
        Background = theme.Transparent;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        var shape = rows ? "Height=\"1\" VerticalAlignment=\"Center\"" : "Width=\"1\" HorizontalAlignment=\"Center\"";
        _thumb.Template = (ControlTemplate)XamlReader.Load(string.Format(TemplateXaml, shape));
        _thumb.Foreground = line ? theme.StrokeStrong : theme.Transparent;
        _thumb.Background = theme.Transparent;
        _thumb.HorizontalAlignment = HorizontalAlignment.Stretch;
        _thumb.VerticalAlignment = VerticalAlignment.Stretch;
        _thumb.DragDelta += (_, args) =>
        {
            var change = rows ? args.VerticalChange : args.HorizontalChange;
            resize(size() + (Reversed ? -change : change));
        };
        _thumb.DragCompleted += (_, _) => Released?.Invoke();
        Children.Add(_thumb);
        ProtectedCursor = InputSystemCursor.Create(rows ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.SizeWestEast);
    }
}
