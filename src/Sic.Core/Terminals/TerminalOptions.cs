namespace Sic.Core.Terminals;

/// <summary>
/// The terminal settings a person can change beyond the font: cursor shape and blink, how many lines of scrollback each
/// pane keeps, and copy on select. Saved values are checked on load, so a hand-edited settings file cannot hand the renderer
/// a cursor it does not know or a buffer that would take the machine's memory.
/// </summary>
public static class TerminalOptions
{
    /// <summary>
    /// The fonts Settings lists, as the CSS font lists the terminal is given; any other installed font can still be typed in
    /// (docs/decisions/terminal-fonts.md). Six that look clearly different: Windows' Cascadia Mono, the default, and Consolas,
    /// then four the terminal page carries itself (<see cref="BundledFonts"/>), so they look the same on every PC. After each
    /// comes Cascadia Mono, for the braille spinners and status symbols the others lack, then the generic monospace.
    /// </summary>
    public static IReadOnlyList<string> FontFamilies { get; } =
    [
        "Cascadia Mono, Consolas, monospace",
        "Consolas, Cascadia Mono, monospace",
        "JetBrains Mono, Cascadia Mono, monospace",
        "Fira Code, Cascadia Mono, monospace",
        "IBM Plex Mono, Cascadia Mono, monospace",
        "Victor Mono, Cascadia Mono, monospace"
    ];

    /// <summary>The fonts the terminal page carries (src/terminal/fonts), by family name.</summary>
    public static IReadOnlyList<string> BundledFonts { get; } = ["JetBrains Mono", "Fira Code", "IBM Plex Mono", "Victor Mono"];

    /// <summary>The cursor shapes xterm.js draws, in the order Settings lists them. The first is the default.</summary>
    public static IReadOnlyList<string> CursorStyles { get; } = ["bar", "block", "underline"];
    public const string DefaultCursorStyle = "bar";

    /// <summary>
    /// Lines of scrollback per pane. xterm.js keeps every line in memory, and a window can hold a dozen panes, so the ceiling
    /// is 50,000 rather than the renderer's own unbounded limit; the floor keeps an agent's last answer reachable.
    /// </summary>
    public const int DefaultScrollback = 10_000, MinScrollback = 1_000, MaxScrollback = 50_000;

    /// <summary>A known cursor style, compared without regard to case; anything else is the default.</summary>
    public static string NormalizeCursorStyle(string? style) =>
        CursorStyles.FirstOrDefault(known => string.Equals(known, style?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? DefaultCursorStyle;

    public static int ClampScrollback(int lines) => Math.Clamp(lines, MinScrollback, MaxScrollback);

    /// <summary>The word Settings shows for a cursor style.</summary>
    public static string CursorLabel(string style) => NormalizeCursorStyle(style) switch
    {
        "block" => "Block",
        "underline" => "Underline",
        _ => "Bar"
    };
}
