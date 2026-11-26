using Sic.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Path = System.IO.Path;

namespace Sic.App;

/// <summary>
/// The active theme as live brushes. Every brush is mutated in place when the theme changes, so the whole
/// tree recolors without being rebuilt. Built-in controls that read WinUI accent resources are re-pointed
/// through <see cref="OverrideAccentResources"/> so they follow the theme too.
/// </summary>
internal sealed class Theme
{
    private readonly Dictionary<string, SolidColorBrush> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SolidColorBrush> _agentFills = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapImage> _agentImages = new(StringComparer.OrdinalIgnoreCase);

    internal ThemeDefinition Definition { get; private set; }
    internal bool Light => Definition.Light;
    /// <summary>
    /// Settings → Chrome, frosted or clear: the rail and the panel are painted part-clear, so the window's backdrop, blurred
    /// or not there at all, shows through them (WindowBackdrop). Inks are still chosen against the solid colors, which the tinted blur comes close to.
    /// </summary>
    internal bool Frosted { get; set; }
    /// <summary>How solid the frosted rail (and the strip and status bar in its color) and the frosted panel are.</summary>
    internal const double RailFrost = 0.55, PanelFrost = 0.72;
    internal SolidColorBrush Base { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Rail { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush RailText { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Panel { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Layer { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Hover { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Pressed { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Stroke { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush StrokeStrong { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Text { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush TextSecondary { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush TextTertiary { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Accent { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush AccentText { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush AccentSoft { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush AccentHover { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush AccentPressed { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>
    /// An accent for ink on the rail: of the family's two accents (the darker one its light theme uses, the lighter one its
    /// dark theme uses), whichever stands out more against the rail. Decided by the rail's color, not the theme's mode, since
    /// SIC light puts a dark rail beside light panels.
    /// </summary>
    internal SolidColorBrush RailAccent { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Success { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Warning { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>
    /// Success, Warning and Danger as a dot's fill on the strip and in the status bar: the theme's hue made vivid for a 6 px dot
    /// (<see cref="StateDots"/>), since the theme's own shades are chosen for text and borders. A pane header's dots are made
    /// the same way against the header's own surface (TabPaint.HeaderSuccess, HeaderWarning). See docs/decisions/state-dots.md;
    /// the danger dot is a tab whose agent reports that it waits for the user or has failed (docs/decisions/program-status.md).
    /// </summary>
    internal SolidColorBrush SuccessDot { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush WarningDot { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush DangerDot { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Danger { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Neutral { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>
    /// The status bar's ink and its usage-limit meters (<see cref="LimitColors"/>, docs/decisions/usage-limits.md): worked out from any
    /// theme, the ink lifted to 4.5:1 on the bar and each state's hue to 3:1, the track and the unknown meter the ink faded.
    /// </summary>
    internal SolidColorBrush BarInk { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush LimitOnPace { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush LimitAhead { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush LimitNear { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush LimitTrack { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush LimitUnknown { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush LimitDivider { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Terminal { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush Transparent { get; } = new(Microsoft.UI.Colors.Transparent);
    // The tab strip sits on the rail color, which in SIC light is dark under a light theme, so its ink is chosen against
    // that surface rather than taken from the theme's body text.
    internal SolidColorBrush StripText { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush StripTextSecondary { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush StripDisabled { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush StripHover { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush StripPressed { get; } = new(Microsoft.UI.Colors.Transparent);
    internal SolidColorBrush StripStroke { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>
    /// The selected tab's fill and the pane header's, so the active tab and its pane read as one surface rising out of the
    /// strip. Where the theme's header color sits too close to the strip to show that, it is moved away from the strip.
    /// </summary>
    internal SolidColorBrush SelectedTab { get; } = new(Microsoft.UI.Colors.Transparent);
    /// <summary>The contrast the active tab keeps against the strip where the theme allows it (near-white on a near-white strip cannot).</summary>
    internal const double ActiveTabLift = 1.6;
    /// <summary>The opaque color behind the tab strip: the rail over the base (Mica themes approximate Mica with the base).</summary>
    internal Color StripSurface { get; private set; }
    /// <summary>The hairline round the active tab's rounded top, as Windows 11's Explorer draws its tabs: the strip's ink at a fifth, where the native card stroke is a tenth and vanishes on a dark strip.</summary>
    internal Color ActiveTabEdge { get; private set; }
    /// <summary>True when the strip is dark, which selects the bright shade of each tab color preset.</summary>
    internal bool StripDark { get; private set; }
    internal FontFamily Display { get; private set; } = new("Segoe UI Variable Display");
    /// <summary>
    /// The faces SicTerm ships in Assets\fonts: Sora, the SicTerm themes' display face and the wordmark's "Sic", and Azeret Mono,
    /// the wordmark's "Term" and the Super-Intelligent-Chat Terminal line. Both are variable fonts, so any weight comes from the
    /// one file; a theme that names either as its display font gets the shipped file, not an installed font that may be missing.
    /// </summary>
    internal static FontFamily Sora { get; } = new("ms-appx:///Assets/fonts/Sora.ttf#Sora, Segoe UI Variable Display");
    internal static FontFamily AzeretMono { get; } = new("ms-appx:///Assets/fonts/AzeretMono.ttf#Azeret Mono, Cascadia Mono, Consolas");
    internal static FontFamily Body { get; } = new("Segoe UI Variable Text");
    internal static FontFamily Mono { get; } = new("Cascadia Mono, Consolas");
    internal static FontFamily Icons { get; } = new("Segoe Fluent Icons");

    internal Theme(ThemeDefinition definition)
    {
        Definition = definition;
        Apply(definition);
    }

    internal void Apply(ThemeDefinition definition)
    {
        Definition = definition;
        var accent = definition.Accent;
        var accentSoft = definition.AccentSoft;
        if (definition.SystemAccent && TryReadSystemAccent(definition.Light, out var system))
        {
            accent = system;
            accentSoft = system + (definition.Light ? "26" : "33");
        }
        Base.Color = Parse(definition.Base); Rail.Color = Parse(definition.Rail); RailText.Color = Parse(definition.RailText);
        Panel.Color = Parse(definition.Panel); Layer.Color = Parse(definition.Layer);
        Hover.Color = Parse(definition.Hover); Pressed.Color = Parse(definition.Pressed);
        Stroke.Color = Parse(definition.Stroke); StrokeStrong.Color = Parse(definition.StrokeStrong);
        Text.Color = Parse(definition.Text); TextSecondary.Color = Parse(definition.TextSecondary); TextTertiary.Color = Parse(definition.TextTertiary);
        Accent.Color = Parse(accent); AccentText.Color = Parse(definition.AccentText); AccentSoft.Color = Parse(accentSoft);
        AccentHover.Color = Blend(Accent.Color, definition.Light ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black, 0.1);
        AccentPressed.Color = Blend(Accent.Color, definition.Light ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black, 0.2);
        Success.Color = Parse(definition.Success); Warning.Color = Parse(definition.Warning);
        Danger.Color = Parse(definition.Danger); Neutral.Color = Parse(definition.Neutral);
        Terminal.Color = Parse(definition.Terminal.Background);
        foreach (var (id, brush) in _agents) brush.Color = Parse(definition.AgentHue(id));
        foreach (var (id, brush) in _agentFills) brush.Color = Parse(definition.AgentFill(id));
        StripSurface = Over(Rail.Color, Base.Color);
        StripDark = Luminance(StripSurface) < 0.4;
        if (Frosted)
        {
            Rail.Color = WithAlpha(StripSurface, RailFrost);
            Panel.Color = WithAlpha(Over(Panel.Color, Base.Color), PanelFrost);
        }
        SuccessDot.Color = Vivid(Success.Color, StripDark); WarningDot.Color = Vivid(Warning.Color, StripDark); DangerDot.Color = Vivid(Danger.Color, StripDark);
        var limits = LimitColors.For(definition);
        BarInk.Color = Parse(limits.Ink);
        LimitOnPace.Color = Parse(limits.OnPace); LimitAhead.Color = Parse(limits.Ahead); LimitNear.Color = Parse(limits.Near);
        LimitTrack.Color = WithAlpha(BarInk.Color, 0.2); LimitUnknown.Color = WithAlpha(BarInk.Color, 0.45); LimitDivider.Color = WithAlpha(BarInk.Color, 0.22);
        var counterpart = ThemeCatalog.Toggle(definition);
        var otherAccent = Parse(counterpart.SystemAccent && TryReadSystemAccent(counterpart.Light, out var systemOther) ? systemOther : counterpart.Accent);
        // A palette's own accent and ink can be fainter on its strip than the strip's glyphs and titles need (Solarized Light,
        // Tokyo Night Day): each then deepens, in its own hue, until it reads.
        RailAccent.Color = Legible(Contrast(Accent.Color, StripSurface) >= Contrast(otherAccent, StripSurface) ? Accent.Color : otherAccent, StripSurface, 3);
        StripText.Color = Legible(Readable(StripSurface, Text.Color, RailText.Color), StripSurface, 4.5);
        StripTextSecondary.Color = Readable(StripSurface, TextSecondary.Color, RailText.Color);
        StripDisabled.Color = WithAlpha(StripText.Color, 0.36);
        StripHover.Color = WithAlpha(StripText.Color, 0.09);
        StripPressed.Color = WithAlpha(StripText.Color, 0.06);
        StripStroke.Color = WithAlpha(StripText.Color, 0.14);
        ActiveTabEdge = WithAlpha(StripText.Color, 0.2);
        SelectedTab.Color = Separate(Over(Layer.Color, Terminal.Color), StripSurface, ActiveTabLift);
        Display = definition.DisplayFont switch { "Sora" => Sora, "Azeret Mono" => AzeretMono, var name => new FontFamily(name) };
    }

    /// <summary>
    /// Points the TabView's own ink, button and separator resources at the strip brushes, so the add and scroll buttons, close
    /// buttons and tab titles stay legible on the rail color. Per-tab fills and title ink are set on each tab by the window.
    /// </summary>
    internal void StyleTabStrip(ResourceDictionary resources)
    {
        var map = new Dictionary<string, Brush>
        {
            ["TabViewItemHeaderCloseButtonForeground"] = StripTextSecondary, ["TabViewItemHeaderPointerOverCloseButtonForeground"] = StripText,
            ["TabViewItemHeaderPressedCloseButtonForeground"] = StripText, ["TabViewItemHeaderSelectedCloseButtonForeground"] = TextSecondary,
            ["TabViewItemSeparator"] = StripStroke,
            ["TabViewButtonForeground"] = StripText, ["TabViewButtonForegroundPointerOver"] = StripText, ["TabViewButtonForegroundPressed"] = StripText,
            ["TabViewButtonForegroundDisabled"] = StripDisabled, ["TabViewButtonBackgroundPointerOver"] = StripHover, ["TabViewButtonBackgroundPressed"] = StripPressed,
            ["TabViewScrollButtonForeground"] = StripText, ["TabViewScrollButtonForegroundPointerOver"] = StripText, ["TabViewScrollButtonForegroundPressed"] = StripText,
            ["TabViewScrollButtonForegroundDisabled"] = StripDisabled, ["TabViewScrollButtonBackgroundPointerOver"] = StripHover, ["TabViewScrollButtonBackgroundPressed"] = StripPressed
        };
        foreach (var (key, brush) in map) Override(resources, key, brush);
    }

    /// <summary>
    /// Points the WinUI resources that color accent buttons, selection pills, focus borders and checked states at the live
    /// accent brushes, so built-in controls follow the theme instead of the Windows accent. Entries are the live brushes, so a
    /// theme switch recolors them in place. Apply to the root and to any control created outside the tree, such as dialogs.
    /// </summary>
    internal void Accentuate(ResourceDictionary resources)
    {
        var map = new Dictionary<string, Brush>
        {
            ["AccentButtonBackground"] = Accent, ["AccentButtonBackgroundPointerOver"] = AccentHover, ["AccentButtonBackgroundPressed"] = AccentPressed,
            ["AccentButtonBorderBrush"] = Transparent, ["AccentButtonBorderBrushPointerOver"] = Transparent, ["AccentButtonBorderBrushPressed"] = Transparent,
            ["AccentButtonForeground"] = AccentText, ["AccentButtonForegroundPointerOver"] = AccentText, ["AccentButtonForegroundPressed"] = AccentText,
            ["ListViewItemSelectionIndicatorBrush"] = Accent, ["ListViewItemSelectionIndicatorPointerOverBrush"] = AccentHover, ["ListViewItemSelectionIndicatorPressedBrush"] = AccentPressed,
            ["SelectorBarItemPillFill"] = Accent,
            ["TextControlBorderBrushFocused"] = Accent,
            ["RadioButtonOuterEllipseCheckedStroke"] = Accent, ["RadioButtonOuterEllipseCheckedStrokePointerOver"] = AccentHover, ["RadioButtonOuterEllipseCheckedStrokePressed"] = AccentPressed,
            ["RadioButtonOuterEllipseCheckedFill"] = Accent, ["RadioButtonOuterEllipseCheckedFillPointerOver"] = AccentHover, ["RadioButtonOuterEllipseCheckedFillPressed"] = AccentPressed,
            ["AccentFillColorDefaultBrush"] = Accent, ["AccentFillColorSecondaryBrush"] = AccentHover, ["AccentFillColorTertiaryBrush"] = AccentPressed,
            ["SystemControlHighlightAccentBrush"] = Accent, ["AccentTextFillColorPrimaryBrush"] = Accent, ["TextOnAccentFillColorPrimaryBrush"] = AccentText
        };
        foreach (var (key, brush) in map) Override(resources, key, brush);
    }

    /// <summary>Writes one resource as a plain entry and into the Default, Dark and Light theme dictionaries so every lookup path finds it.</summary>
    internal static void Override(ResourceDictionary resources, string key, object value)
    {
        resources[key] = value;
        foreach (var themeKey in new object[] { "Default", "Dark", "Light" })
        {
            if (!resources.ThemeDictionaries.TryGetValue(themeKey, out var existing) || existing is not ResourceDictionary dictionary)
                resources.ThemeDictionaries[themeKey] = dictionary = new ResourceDictionary();
            dictionary[key] = value;
        }
    }
    private static bool TryReadSystemAccent(bool light, out string hex)
    {
        try
        {
            var settings = new Windows.UI.ViewManagement.UISettings();
            var color = settings.GetColorValue(light ? Windows.UI.ViewManagement.UIColorType.AccentDark1 : Windows.UI.ViewManagement.UIColorType.AccentLight2);
            hex = $"{color.R:X2}{color.G:X2}{color.B:X2}";
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            hex = "";
            return false;
        }
    }

    internal static Color Parse(string hex)
    {
        if (!ThemeCatalog.IsHex(hex)) throw new ArgumentException("Theme colors must be RRGGBB or RRGGBBAA: " + hex, nameof(hex));
        var alpha = hex.Length == 8 ? Convert.ToByte(hex[6..8], 16) : (byte)255;
        return Color.FromArgb(alpha, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    internal static Color Blend(Color color, Color toward, double amount) => Color.FromArgb(color.A,
        (byte)Math.Round(color.R + (toward.R - color.R) * amount), (byte)Math.Round(color.G + (toward.G - color.G) * amount), (byte)Math.Round(color.B + (toward.B - color.B) * amount));

    internal static Color WithAlpha(Color color, double opacity) => Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B);

    /// <summary>A translucent color composited over an opaque one.</summary>
    internal static Color Over(Color top, Color bottom) => Blend(Color.FromArgb(255, bottom.R, bottom.G, bottom.B), Color.FromArgb(255, top.R, top.G, top.B), top.A / 255.0);

    /// <summary>A state color as a dot on the surface: the same hue, vivid for the surface's darkness (StateDots).</summary>
    internal static Color Vivid(Color color, Color surface) => Vivid(color, Luminance(surface) < 0.4);

    internal static Color Vivid(Color color, bool onDark)
    {
        var (r, g, b) = StateDots.Vivid(color.R, color.G, color.B, onDark);
        return Color.FromArgb(255, r, g, b);
    }

    /// <summary>WCAG relative luminance of the opaque part of a color.</summary>
    internal static double Luminance(Color color)
    {
        static double Channel(byte value) { var c = value / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>WCAG contrast ratio of ink (translucent allowed) drawn on an opaque surface.</summary>
    internal static double Contrast(Color ink, Color surface)
    {
        double a = Luminance(Over(ink, surface)), b = Luminance(surface);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    /// Moves a surface away from another, lighter or darker as it already leans, until the two reach the ratio or it runs out of
    /// room. On a dark strip a surface that leans darker and runs out of room rises instead (Harbor dark, whose header is darker
    /// than its strip): an active tab darker than its strip reads as pressed. A near-white surface on a light strip stays where it
    /// is; darker would read as pressed too, and the active tab's edge does the work there.
    /// </summary>
    internal static Color Separate(Color surface, Color from, double ratio)
    {
        var toward = Luminance(surface) >= Luminance(from) ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var moved = Move(surface, from, ratio, toward);
        if (toward == Microsoft.UI.Colors.Black && Contrast(moved, from) < ratio && Luminance(from) < 0.4)
        {
            var risen = Move(surface, from, ratio, Microsoft.UI.Colors.White);
            if (Contrast(risen, from) > Contrast(moved, from)) moved = risen;
        }
        return moved;
    }

    private static Color Move(Color surface, Color from, double ratio, Color toward)
    {
        var moved = surface;
        for (var amount = 0.01; Contrast(moved, from) < ratio && amount <= 0.3; amount += 0.01) moved = Blend(surface, toward, amount);
        return moved;
    }

    /// <summary>The preferred ink when it reaches 4.5:1 on the surface, otherwise whichever of the two reads better.</summary>
    internal static Color Readable(Color surface, Color preferred, Color fallback) =>
        Contrast(preferred, surface) >= 4.5 || Contrast(preferred, surface) >= Contrast(fallback, surface) ? preferred : fallback;

    /// <summary>The ink as it is when it reaches the ratio on the surface; otherwise moved toward black or white, whichever the surface reads better with, only as far as the ratio needs.</summary>
    internal static Color Legible(Color ink, Color surface, double ratio)
    {
        var solid = Over(ink, surface);
        var toward = Ink(surface);
        var moved = solid;
        for (var amount = 0.02; Contrast(moved, surface) < ratio && amount <= 1; amount += 0.02) moved = Blend(solid, toward, amount);
        return Contrast(ink, surface) >= ratio ? ink : moved;
    }

    /// <summary>Black or white, whichever reads better on the surface.</summary>
    internal static Color Ink(Color surface) =>
        Contrast(Microsoft.UI.Colors.White, surface) >= Contrast(Microsoft.UI.Colors.Black, surface) ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;

    internal static SolidColorBrush Brush(string hex) => new(Parse(hex));
    internal static string Hex(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";

    internal SolidColorBrush Agent(string id)
    {
        if (!_agents.TryGetValue(id, out var brush)) _agents[id] = brush = Brush(Definition.AgentHue(id));
        return brush;
    }

    internal SolidColorBrush AgentFill(string id)
    {
        if (!_agentFills.TryGetValue(id, out var brush)) _agentFills[id] = brush = Brush(Definition.AgentFill(id));
        return brush;
    }

    internal static string AgentName(string id) => id switch { "hermes" => "Hermes", "claude" => "Claude Code", "codex" => "Codex", "opencode" => "OpenCode", _ => "PowerShell" };

    /// <summary>Where the agents' own icons are cached (docs/decisions/agent-icons.md); none, and every agent has its letter tile.</summary>
    internal AgentIconCache? AgentIcons { get; set; }

    /// <summary>The agent's own icon at a tile of this size, from <see cref="AgentIcons"/>; null when it has none and gets its letter.</summary>
    internal ImageSource? AgentImage(string id, double size = 24)
    {
        if (id == "shell" || AgentIcons?.FramePath(id, size) is not { } path) return null;
        if (!_agentImages.TryGetValue(path, out var image))
            // The file is rewritten in place when the tool's icon changes, so the image is read from it, not from WinUI's cache.
            _agentImages[path] = image = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache, UriSource = new Uri(path) };
        return image;
    }

    /// <summary>
    /// The single agent mark: the tool's own icon where SIC found one, otherwise a tinted rounded tile with the agent's letter in
    /// its hue, or for PowerShell the terminal window of <see cref="TerminalArt"/>. Each tile is kept track of, so it follows when
    /// detection finds the icon or loses it.
    /// </summary>
    internal Border AgentTile(string id, double size = 24)
    {
        var tile = new Border { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        PaintAgentTile(tile, id, size);
        // Every conversation row makes a tile, and the list is rebuilt on each search; the dead references are swept now and
        // then here, not only when an icon changes, so the list does not grow for the life of the window.
        if (_agentTiles.Count >= _sweepTilesAt) { _agentTiles.RemoveAll(entry => !entry.Tile.TryGetTarget(out _)); _sweepTilesAt = Math.Max(1024, _agentTiles.Count * 2); }
        _agentTiles.Add((id, size, new WeakReference<Border>(tile)));
        return tile;
    }

    private int _sweepTilesAt = 1024;

    /// <summary>Redraws every live tile of these agents, after their cached icons changed; tiles no longer on screen are dropped.</summary>
    internal void RefreshAgentTiles(IReadOnlyCollection<string> ids)
    {
        foreach (var key in _agentImages.Keys.ToArray())
            if (ids.Any(id => Path.GetFileName(key).StartsWith(id + "-", StringComparison.OrdinalIgnoreCase))) _agentImages.Remove(key);
        _agentTiles.RemoveAll(entry => !entry.Tile.TryGetTarget(out _));
        foreach (var (id, size, reference) in _agentTiles)
            if (ids.Contains(id) && reference.TryGetTarget(out var tile)) PaintAgentTile(tile, id, size);
    }

    private readonly List<(string Id, double Size, WeakReference<Border> Tile)> _agentTiles = [];

    private void PaintAgentTile(Border tile, string id, double size)
    {
        var icon = AgentImage(id, size);
        // Art made for an app's Start tile (Codex's desktop app: the white knot) sits on a tile of the colour its manifest names.
        var plate = icon is null ? null : AgentIcons?.CachedSource(id)?.Plate;
        tile.CornerRadius = icon is null || plate is not null ? new CornerRadius(Math.Max(4, size / 5)) : new CornerRadius(0);
        tile.Background = icon is null ? id == "shell" ? TerminalBody() : AgentFill(id) : plate is not null ? Brush(plate) : null;
        // Start-tile art fills its tile nearly to the edge; inset by an eighth a side it sits on the plate as the app's own tile does.
        var art = plate is null ? size : Math.Round(size * 0.76);
        tile.Child = icon is not null
            ? new Image { Source = icon, Stretch = Stretch.Uniform, Width = art, Height = art, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
            : id == "shell" ? TerminalArt(size)
            : new TextBlock
            {
                Text = Sic.Core.Agents.AgentIcons.Letter(id), FontFamily = Display, FontSize = Math.Max(9, size * 0.58), Foreground = Agent(id),
                FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 }, TextLineBounds = TextLineBounds.Tight,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
    }

    /// <summary>The window behind <see cref="TerminalArt"/>: near-black, a shade lighter at the top, on every theme.</summary>
    private static Brush TerminalBody()
    {
        var stops = new GradientStopCollection { new GradientStop { Color = Parse("2A2A2A"), Offset = 0 }, new GradientStop { Color = Parse("161616"), Offset = 1 } };
        return new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1), GradientStops = stops };
    }

    /// <summary>
    /// PowerShell's tile: a terminal window SIC draws, after the manner of Windows Terminal's icon but not its art, since SIC
    /// ships no other company's mark (docs/decisions/agent-icons.md). On the dark window of <see cref="TerminalBody"/>, a lighter
    /// title bar and a white chevron and underscore; over them a faint white rim, which keeps the tile's edge on a dark panel and
    /// goes unseen on a light one. Laid out on the tile's own size, so it is sharp at every scale; the prompt's stroke has a
    /// floor, so it still reads at a pane header's 14 px.
    /// </summary>
    private static Grid TerminalArt(double size)
    {
        static Windows.Foundation.Point At(double x, double y) => new(x, y);
        var radius = Math.Max(4, size / 5);
        // A whole number of DIPs, so the bar's edge is crisp at 100 %.
        var bar = Math.Round(size * 0.22);
        // The prompt is centred in the window below the bar, the chevron a little over half its height, the underscore on its foot.
        var middle = bar + (size - bar) / 2;
        var reach = (size - bar) * 0.28;
        var chevron = new PathFigure
        {
            StartPoint = At(size * 0.23, middle - reach), IsClosed = false,
            Segments = new PathSegmentCollection { new PolyLineSegment { Points = new PointCollection { At(size * 0.46, middle), At(size * 0.23, middle + reach) } } }
        };
        var prompt = new GeometryGroup
        {
            Children = new GeometryCollection
            {
                new PathGeometry { Figures = new PathFigureCollection { chevron } },
                new LineGeometry { StartPoint = At(size * 0.55, middle + reach), EndPoint = At(size * 0.77, middle + reach) }
            }
        };
        var art = new Grid { Width = size, Height = size };
        art.Children.Add(new Border { Height = bar, VerticalAlignment = VerticalAlignment.Top, Background = Brush("4A4A4A"), CornerRadius = new CornerRadius(radius, radius, 0, 0) });
        art.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = prompt, Stroke = new SolidColorBrush(Microsoft.UI.Colors.White), StrokeThickness = Math.Max(1.75, size * 0.115),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        });
        art.Children.Add(new Border { BorderBrush = Brush("FFFFFF2E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius) });
        return art;
    }

    internal static FontIcon Icon(string glyph, double size = 16, Brush? foreground = null)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = size, FontFamily = Icons, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (foreground is not null) icon.Foreground = foreground;
        return icon;
    }

    internal TextBlock Caption(string text) => new() { Text = text, FontSize = 12, FontFamily = Body, Foreground = TextSecondary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    internal TextBlock Tertiary(string text) => new() { Text = text, FontSize = 12, FontFamily = Body, Foreground = TextTertiary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    internal TextBlock BodyText(string text, double size = 14, ushort weight = 400) => new()
    {
        Text = text, FontSize = size, FontFamily = Body, Foreground = Text, TextTrimming = TextTrimming.CharacterEllipsis,
        FontWeight = new Windows.UI.Text.FontWeight { Weight = weight }, VerticalAlignment = VerticalAlignment.Center
    };
    internal TextBlock Heading(string text, double size = 14) => new()
    {
        Text = text, FontSize = size, FontFamily = Display, Foreground = Text, TextTrimming = TextTrimming.CharacterEllipsis,
        FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, VerticalAlignment = VerticalAlignment.Center
    };
    internal TextBlock Eyebrow(string text) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 11, FontFamily = Display, Foreground = TextSecondary, CharacterSpacing = 60,
        FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, VerticalAlignment = VerticalAlignment.Center
    };
    internal TextBlock MonoText(string text) => new() { Text = text, FontSize = 12, FontFamily = Mono, Foreground = TextSecondary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

    internal Button IconButton(string glyph, string tooltip, double size = 32, double glyphSize = 16, Brush? foreground = null)
        => MarkButton(Icon(glyph, glyphSize, foreground ?? TextSecondary), tooltip, size);

    /// <summary>An icon button around drawn content rather than a font glyph: the pinned mark, say.</summary>
    internal Button MarkButton(UIElement mark, string tooltip, double size = 32)
    {
        var button = new Button
        {
            Content = mark, Width = size, Height = size, Padding = new Thickness(0),
            Background = Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    internal static Button TextButton(string text, string? tooltip = null)
    {
        var button = new Button { Content = text, MinHeight = 32, Padding = new Thickness(12, 4, 12, 5), FontFamily = Body };
        if (tooltip is not null) ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    internal Button PrimaryButton(string text, string? tooltip = null)
    {
        var button = TextButton(text, tooltip);
        button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        Accentuate(button.Resources);
        button.FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 };
        return button;
    }

    internal static Button IconTextButton(string glyph, string text, string? tooltip = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(Icon(glyph, 14));
        content.Children.Add(new TextBlock { Text = text, FontFamily = Body, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, MinHeight = 32, Padding = new Thickness(10, 4, 12, 5) };
        if (tooltip is not null) ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    internal static Ellipse Dot(Brush fill, double size = 6) => new() { Width = size, Height = size, Fill = fill, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The outline pin, on every pin to the top of Sessions: the rows' buttons, the tab's and the pane's menu items.</summary>
    internal const string PinGlyph = "";
    /// <summary>The unpin glyph beside every Unpin item.</summary>
    internal const string UnpinGlyph = "";
    /// <summary>The tooltip of every Keep panes even item (docs/decisions/keep-panes-even.md).</summary>
    internal const string KeepEvenTip = "Every pane the same share of the tab, kept so through splits and closes; dragging a divider turns it off";

    /// <summary>
    /// A "Split right" or "Split down" menu item with the direction's shortcut, for a tab's menu and a pane's: one glyph for both
    /// directions, turned for the split into rows. The caller adds the click and the enabling.
    /// </summary>
    internal static MenuFlyoutItem SplitMenuItem(Sic.Core.State.PaneSplit direction)
    {
        var columns = direction == Sic.Core.State.PaneSplit.Columns;
        var icon = new FontIcon { Glyph = "", FontFamily = Icons };
        if (!columns) { icon.RenderTransform = new RotateTransform { Angle = 90 }; icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5); }
        return new MenuFlyoutItem { Text = columns ? "Split right" : "Split down", KeyboardAcceleratorTextOverride = columns ? "Alt+Shift+Plus" : "Alt+Shift+Minus", Icon = icon };
    }

    /// <summary>A rule across a page or a list, in the theme's stroke.</summary>
    internal Border Divider() => new() { Height = 1, Background = Stroke, HorizontalAlignment = HorizontalAlignment.Stretch };
}
