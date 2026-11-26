namespace Sic.Core.Appearance;

/// <summary>
/// Settings → Menu Animation (Slide Menu until October 15, 2026): whether the panel slides open and shut, or snaps. On or off, whatever Windows' animation effects
/// setting says (docs/decisions/motion-setting.md); the notices, the working ring and the Your turn blink always move. Kept as a word so a settings file reads plainly,
/// and normalized on load: the earlier "always" and "never" are on and off, and "system" or an unknown word is on, the default.
/// </summary>
public static class MotionSetting
{
    public const string On = "on", Off = "off";
    public static IReadOnlyList<string> Choices { get; } = [On, Off];

    public static string Normalize(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        Off or "never" or "false" => Off,
        _ => On
    };

    /// <summary>Whether to animate.</summary>
    public static bool Resolve(string? setting) => Normalize(setting) == On;

    public static string Label(string? setting) => Normalize(setting) == On ? "On" : "Off";
}
