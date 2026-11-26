using Sic.Core.State;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Sic.App;

internal sealed partial class MainWindow
{
    /// <summary>The first-run size, and the size smoke stage 1 is fixed at: its screenshots and column counts depend on it.</summary>
    private static readonly WindowPlacement DefaultPlacement = new(0, 0, 1440, 900, false);
    /// <summary>The last bounds the window had while neither maximized nor minimized; what a maximized window is saved with.</summary>
    private PixelRect _restoredBounds;
    private bool _placementTracked;

    /// <summary>
    /// Puts the window where it was left, fitted to the display it lands on. The first run, and smoke stage 1, take the fixed
    /// default size; the restore stage deliberately goes through the saved placement, since that is what it checks.
    /// </summary>
    private void PlaceWindow()
    {
        var saved = _options.SmokeTest && !_options.RestoreOnly ? null : _preferences.Window;
        if (saved is null)
        {
            AppWindow.Resize(new SizeInt32(DefaultPlacement.Width, DefaultPlacement.Height));
            _restoredBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            return;
        }
        var placed = WindowPlacement.Fit(saved, WorkAreaFor(saved), DefaultPlacement);
        AppWindow.MoveAndResize(new RectInt32(placed.X, placed.Y, placed.Width, placed.Height));
        _restoredBounds = new(placed.X, placed.Y, placed.Width, placed.Height);
        if (placed.Maximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    /// <summary>The work area of the display the saved bounds are on, or of the nearest one when that display is gone.</summary>
    private static PixelRect WorkAreaFor(WindowPlacement saved)
    {
        var area = DisplayArea.GetFromRect(new RectInt32(saved.X, saved.Y, Math.Max(1, saved.Width), Math.Max(1, saved.Height)), DisplayAreaFallback.Nearest).WorkArea;
        return new(area.X, area.Y, area.Width, area.Height);
    }

    /// <summary>
    /// Records every move, resize, maximize and restore into the preferences, which <see cref="SavePreferences"/> writes once
    /// they have been quiet, so a drag does not write the file per frame. Maximize and restore arrive as size and position
    /// changes (DidPresenterChange is only for a different presenter object), so the presenter's state decides what they mean;
    /// a minimized window is ignored.
    /// </summary>
    private void TrackWindowPlacement()
    {
        if (_placementTracked) return;
        _placementTracked = true;
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange) RecordPlacement();
        };
    }

    private void RecordPlacement()
    {
        // The way into and out of full screen arrives as size changes at the display's bounds while the presenter still reads as
        // the windowed one; those are not placements (docs/decisions/full-screen.md), and the bounds before F11 are kept as they were.
        if (_fullScreen || AppWindow.Presenter is not OverlappedPresenter presenter || !_placementTracked) return;
        WindowPlacement placement;
        switch (presenter.State)
        {
            case OverlappedPresenterState.Restored:
                _restoredBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
                placement = new(_restoredBounds.X, _restoredBounds.Y, _restoredBounds.Width, _restoredBounds.Height, false);
                break;
            case OverlappedPresenterState.Maximized:
                placement = new(_restoredBounds.X, _restoredBounds.Y, _restoredBounds.Width, _restoredBounds.Height, true);
                break;
            default: return;
        }
        if (placement == _preferences.Window) return;
        _preferences.Window = placement;
        SavePreferences();
    }
}
