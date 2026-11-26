namespace Sic.Core.Appearance;

/// <summary>
/// The icon SIC hands the shell: the taskbar button, the title bar (which Alt+Tab shows too) and the tray. The app takes the
/// shipped one, <see cref="Default"/>, in its light-taskbar or dark-taskbar file. Up to October 8, 2026 Settings also offered
/// the design rounds' candidates (simple-17 and simple-18), so each could be judged on the real taskbar of
/// every PC SIC runs on; they stay listed here for a later review, and the word in settings.json, once normalized on load, is
/// now set to the default on load, so a saved candidate is dropped. The exe's own icon, which File Explorer, Start and
/// shortcuts show, is the shipped one too. Up to 0.9.3 the shipped word was "motion-fitted", which falls back to the default.
/// </summary>
public static class AppIcon
{
    /// <summary>
    /// One choice. <see cref="Icon"/> and <see cref="LightTaskbarIcon"/> name .ico files under Assets\brand, without the
    /// extension: the second is for a light taskbar, for the designs drawn in an ink that cannot hold contrast on both.
    /// The 16 to 48 px frames of each are also there as <c>&lt;file&gt;-&lt;size&gt;.png</c>, for the Settings previews.
    /// </summary>
    public sealed record Option(string Id, string Title, string Note, string Icon, string? LightTaskbarIcon = null);

    public const string Default = "shipped";

    /// <summary>The frame sizes a preview may use: every tray and taskbar size from 100% to 200% scaling.</summary>
    public static IReadOnlyList<int> PreviewSizes { get; } = [16, 20, 24, 28, 30, 32, 36, 40, 42, 48];

    public static IReadOnlyList<Option> All { get; } =
    [
        new(Default, "Prompt and arrow (shipped)",
            "simple-21 A, from the owner's sketch. A >_ prompt inside a circular arrow that runs counter-clockwise round it and climbs into the _'s end. White and cyan on a dark taskbar, near-black and teal on a light one.", "sic", "sic-light"),
        new("motion-previous", "Motion mark, fitted (until 0.9.3)",
            "simple-18 E, shipped up to 0.9.3. The approved mark redrawn on each size's own pixel grid: at 24 px the coral hub with its white > and three teal arrows. 16 and 20 px, the tray and the title bar, stay soft.", "options/motion-previous"),
        new("motion-approved", "Motion mark, as approved",
            "simple-18 D. The approved small cut to 32 px and the full mark above, unchanged, each rendered at its exact size: the arrows at every size, softer than the fitted one.", "options/motion-approved"),
        new("lit-hub", "Lit hub",
            "simple-17 A. The coral hub alone at every taskbar size: a gradient disc with a round-capped white >. The arrows come back from 56 px, for Start and File Explorer.", "options/lit-hub"),
        new("orbit", "Orbit",
            "simple-17 B. The hub alone up to 24 px; from 28 px (125% scaling and up) light-cyan arrows ring a smaller hub. Deep teal on a light taskbar.", "options/orbit", "options/orbit-light"),
        new("coral-tile", "Coral tile",
            "simple-17 C. A white > on a coral rounded square, like the Microsoft 365 tiles. It breaks the no-plate rule, so it is here to compare.", "options/coral-tile"),
        new("mono-line", "Mono line",
            "simple-17 D. Line art in one ink, white on a dark taskbar and black on a light one: a > with three arrows running round it; one ring arrow at 16 and 20 px.", "options/mono-line-dark", "options/mono-line"),
        new("mono-disc", "Mono disc",
            "simple-17 E. The line art knocked out of a solid disc, white on a dark taskbar and black on a light one: the > alone up to 32 px, the arrows from 36 px.", "options/mono-disc-dark", "options/mono-disc"),
    ];

    public static string Normalize(string? id)
    {
        var word = id?.Trim().ToLowerInvariant();
        return All.Any(option => option.Id == word) ? word! : Default;
    }

    public static Option Find(string? id) => All.First(option => option.Id == Normalize(id));

    /// <summary>The .ico file name, under Assets\brand and without the extension, for a taskbar that is light or dark.</summary>
    public static string IconFor(Option option, bool lightTaskbar) => lightTaskbar && option.LightTaskbarIcon is { } light ? light : option.Icon;

    /// <summary>
    /// The preview frame for a slot of <paramref name="pixels"/> physical pixels: that size when the option has it, otherwise the
    /// next larger one it has, otherwise its largest. <paramref name="has"/> answers whether a size's frame exists.
    /// </summary>
    public static int PreviewSize(int pixels, Func<int, bool> has)
    {
        foreach (var size in PreviewSizes) if (size >= pixels && has(size)) return size;
        return PreviewSizes.Reverse().FirstOrDefault(has, PreviewSizes[0]);
    }

    /// <summary>
    /// The relative luminance above which a surface takes the near-black (#111111) ink rather than the white: where the two
    /// reach the same contrast against it, (1 + 0.05) / (L + 0.05) = (L + 0.05) / (0.0056 + 0.05), so L is about 0.19.
    /// </summary>
    public const double DarkInkAbove = 0.19;

    /// <summary>Whether a surface of this WCAG relative luminance reads the near-black ink better than the white.</summary>
    public static bool PrefersDarkInk(double relativeLuminance) => relativeLuminance > DarkInkAbove;

    /// <summary>
    /// Whether the taskbar is light enough for the near-black icon. Windows' mode (SystemUsesLightTheme) says light or dark,
    /// but with Show accent color on Start and taskbar (ColorPrevalence) the taskbar is painted in the accent's Start colour
    /// (StartColorMenu, ABGR), which current Windows 11 allows in light mode too: measured on the owner's PC on October 1,
    /// 2026, light mode with the accent on, StartColorMenu #1D6978, and a taskbar Icon Branding Runway sampled at #114553 and
    /// #436D79, dark either way. So with the accent on, the accent colour's lightness decides; without it, the mode.
    /// </summary>
    public static bool LightTaskbar(bool lightMode, bool accentOnTaskbar, int? startColorAbgr)
    {
        if (!accentOnTaskbar || startColorAbgr is not { } abgr) return lightMode;
        return PrefersDarkInk(RelativeLuminance((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF)));
    }

    /// <summary>WCAG relative luminance of an sRGB colour.</summary>
    public static double RelativeLuminance(byte red, byte green, byte blue)
    {
        static double Channel(byte value) { var c = value / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(red) + 0.7152 * Channel(green) + 0.0722 * Channel(blue);
    }
}
