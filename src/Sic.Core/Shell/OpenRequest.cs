namespace Sic.Core.Shell;

/// <summary>
/// What <c>SicTerm.exe --open &lt;folder&gt; [--agent &lt;id&gt;]</c> asks for: the New session dialog with that folder filled in, or
/// with an agent, a session of that agent started there at once. When a SIC already runs with the data folder, the second
/// process hands the request to it in a <c>WM_COPYDATA</c> message and exits; <see cref="Encode"/> and <see cref="Decode"/> are the
/// two ends of that message, whose <c>dwData</c> is <see cref="CopyDataId"/>.
/// </summary>
public sealed record OpenRequest(string Folder, string? AgentId = null)
{
    public const int CopyDataId = 1;

    /// <summary>The folder, a line break, and the agent id or nothing: one string for the message.</summary>
    public string Encode() => Folder + "\n" + (AgentId ?? "");

    /// <summary>The request a message carried, or null when the text is not one.</summary>
    public static OpenRequest? Decode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = text.Split('\n', 2);
        var folder = parts[0].TrimEnd('\r');
        if (folder.Length == 0 || folder.Contains('\0')) return null;
        var agent = parts.Length > 1 ? parts[1].TrimEnd('\0', '\r') : "";
        return new OpenRequest(folder, agent.Length > 0 ? agent : null);
    }

    /// <summary>What is wrong with a folder as it reached SIC, or null when it is an absolute path to a folder that exists.</summary>
    public static string? Problem(string folder, Func<string, bool> exists)
    {
        if (!Path.IsPathFullyQualified(folder)) return "“" + folder + "” is not an absolute folder path.";
        if (!exists(folder)) return "The folder “" + folder + "” does not exist.";
        return null;
    }
}
