using Sic.Core.Agents;

namespace Sic.Core.Limits;

/// <summary>
/// The ChatGPT plan behind Codex, Hermes and OpenCode (docs/decisions/usage-limits.md). Codex always uses it; Hermes does when
/// a pane's session is on the <see cref="HermesProvider"/> (<c>openai-codex</c>) provider, or its config names it before the
/// session says; OpenCode when the pane's session is on the <see cref="OpenCodeProvider"/> (<c>openai</c>) provider and the plan
/// has been seen on this PC, since an OpenAI API key gives that provider too. The plan's windows come from Codex's session logs
/// (<see cref="UsageLimits.FromCodexRollout"/>) and from Hermes's own usage command (<see cref="HermesUsage"/>), which Hermes
/// fetches with its own login: SIC sees no credential either way. The latest reading is kept in the data folder
/// (<see cref="ReadingFile"/>), so the plan counts as seen across restarts.
/// </summary>
public static class ChatGptPlan
{
    public const string HermesProvider = HermesUsage.ChatGptProvider;
    public const string OpenCodeProvider = "openai";
    public const string ReadingFile = "chatgpt.json";

    /// <summary>
    /// The provider in Hermes's config.yaml (<c>model.provider</c>), or null when the file or the key is missing: the model
    /// block's first <c>provider:</c> line, read as <see cref="HermesContext.ModelConfig"/> reads the block. What a session
    /// draws on before its row says (<see cref="HermesContext.Provider"/>).
    /// </summary>
    public static string? HermesConfiguredProvider(string hermesHome) => HermesContext.ConfigValue(hermesHome, "provider");

    /// <summary>The kept ChatGPT reading in the data folder, or null.</summary>
    public static LimitReading? Read(string dataDirectory)
    {
        try { return UsageLimits.Deserialize(File.ReadAllText(Path.Combine(ClaudeStatusLine.LimitsFolder(dataDirectory), ReadingFile))); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Keeps the reading in the data folder, replacing the file whole.</summary>
    public static void Write(string dataDirectory, LimitReading reading) => ClaudeStatusLine.WriteReading(dataDirectory, ReadingFile, reading);
}
