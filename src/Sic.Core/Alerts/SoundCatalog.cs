namespace Sic.Core.Alerts;

/// <summary>
/// Every sound SIC can ring: the built-in recipes (<see cref="SoundRecipes"/>, the chime first), then the user's own WAV files
/// from the sounds folder under SIC's data folder, read by <see cref="Reload"/> at launch and on request
/// (docs/decisions/completion-sounds.md). A file that cannot be used is listed in <see cref="Problems"/> with its error and is
/// not offered. A sound is found by its id: a recipe's word, or a file's name.
/// </summary>
public static class SoundCatalog
{
    /// <summary>The longest any sound plays: a file longer than this plays its first <see cref="MaxSeconds"/> seconds, faded at the cut.</summary>
    public const double MaxSeconds = 2.5;
    private static readonly IReadOnlyList<Sound> BuiltInSounds = SoundRecipes.All.Select(SoundRecipes.ToSound).ToList();
    private static IReadOnlyList<Sound> _all = BuiltInSounds;
    private static IReadOnlyList<SoundProblem> _problems = [];

    /// <summary>The sounds that ship, in the order the list shows them first. The first is the default, the chime.</summary>
    public static IReadOnlyList<Sound> BuiltIn => BuiltInSounds;

    /// <summary>Every sound: the built-in ones, then the user's files in file-name order, as of the last <see cref="Reload"/>.</summary>
    public static IReadOnlyList<Sound> All => _all;

    /// <summary>The user's sound files that could not be used at the last <see cref="Reload"/>, each with its error.</summary>
    public static IReadOnlyList<SoundProblem> Problems => _problems;

    /// <summary>The user's sounds folder last read; null until <see cref="Reload"/> has run.</summary>
    public static string? Folder { get; private set; }

    public static Sound Default => BuiltInSounds[0];

    /// <summary>
    /// Reads the user's sound files from <paramref name="folder"/> and puts them after the built-in sounds; a null folder, or one
    /// that does not exist, leaves the built-in sounds alone. Files are checked here and decoded when first played. Returns the
    /// files that could not be used.
    /// </summary>
    public static IReadOnlyList<SoundProblem> Reload(string? folder)
    {
        Folder = folder;
        if (folder is null) { _all = BuiltInSounds; _problems = []; return _problems; }
        var (sounds, problems) = SoundFiles.ReadFolder(folder, MaxSeconds, ReadyChime.Peak);
        _all = [.. BuiltInSounds, .. sounds];
        _problems = problems;
        return problems;
    }

    public static bool IsBuiltIn(string? id) => BuiltInSounds.Any(sound => Sound.SameId(sound.Id, id));

    /// <summary>The sound with the id, or the default for an unknown one, such as a file since removed.</summary>
    public static Sound Find(string? id) => All.FirstOrDefault(sound => Sound.SameId(sound.Id, id)) ?? Default;

    /// <summary>A saved id as it is kept: trimmed, the default for nothing.</summary>
    public static string Normalize(string? id) => string.IsNullOrWhiteSpace(id) ? Default.Id : id.Trim();
}
