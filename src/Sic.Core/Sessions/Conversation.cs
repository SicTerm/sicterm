namespace Sic.Core.Sessions;

/// <summary>
/// One saved agent conversation as the catalog knows it. <see cref="TextIndexed"/> is false when only identity, folder,
/// timing and a bounded first-message excerpt were imported (see docs/decisions/large-transcripts.md); such a
/// conversation still previews and resumes exactly, but its message text is not searchable.
/// </summary>
public sealed record Conversation(string AgentId, string SessionId, string Title, string? WorkingDirectory,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, string SourcePath, string HistoryDirectory, string Body,
    bool TextIndexed = true, string? TextNote = null)
{
    public string Key => AgentId + ":" + SessionId;
}

