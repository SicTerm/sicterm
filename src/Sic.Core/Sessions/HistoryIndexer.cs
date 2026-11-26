using System.Collections.Concurrent;
using Sic.Core.Agents;

namespace Sic.Core.Sessions;

/// <summary>
/// <see cref="MetadataOnly"/> counts transcripts above the full-text limit that were imported as identity plus a bounded excerpt.
/// <see cref="Unlisted"/> names the sessions in an agent's database left out because their id is in a form SicTerm does not
/// resume; the database's other sessions were read (docs/decisions/unlisted-sessions.md).
/// </summary>
public sealed record IndexResult(int Imported, int Unchanged, int Failed, IReadOnlyList<string> Warnings, int MetadataOnly = 0,
    IReadOnlyList<(AgentDefinition Agent, string SessionId)>? Unlisted = null);

/// <summary>
/// Imports agent histories into the rebuildable catalog. A transcript above <see cref="DefaultFullTextLimit"/> is not
/// skipped: it is streamed once for identity, folder, title, timing and a first-message excerpt and stored with
/// <see cref="Conversation.TextIndexed"/> = false. Full-text indexing of such files is deferred; see docs/decisions/large-transcripts.md.
/// Each source's fingerprint (size and write time) is kept in the catalog with the conversations it produced, so a launch
/// re-reads only the sources that changed since the catalog last saw them. The transcripts that did change are parsed
/// several at a time (<see cref="TranscriptParser"/>) and written one at a time, since parsing is what a refresh spends its
/// time on: on a real history the three largest transcripts, 16 to 35 MB each, parsed in 335 ms one after another and in
/// 104 ms together, while writing their rows took 2 to 16 ms (October 16, 2026).
/// </summary>
public sealed class HistoryIndexer(ConversationCatalog catalog, long fullTextLimit = HistoryIndexer.DefaultFullTextLimit)
{
    /// <summary>Above this size the whole message text is not loaded; the parser and the catalog still hold a transcript's text in memory in one piece.</summary>
    public const long DefaultFullTextLimit = 64L * 1024 * 1024;
    /// <summary>
    /// Part of every fingerprint. Raise it when what an import writes for an unchanged source changes (a reader reads more, a title
    /// rule changes, the full-text limit moves), so that every source is read again once. 3: Codex's AGENTS.md preamble no longer
    /// titles a conversation (October 24, 2026).
    /// </summary>
    private const int ImportVersion = 3;
    /// <summary>
    /// Writes made durable this many conversations, or this long, apart: a long first import is not lost whole to a quit midway,
    /// and a write from elsewhere (a new session's row, say) waits at most this long for the import's transaction.
    /// </summary>
    private const int CommitEvery = 200;
    private static readonly TimeSpan CommitAfter = TimeSpan.FromMilliseconds(500);
    private readonly ConversationCatalog _catalog = catalog;
    private readonly long _fullTextLimit = fullTextLimit;
    private Dictionary<string, string>? _fingerprints;

    public IndexResult Refresh(IEnumerable<AgentDefinition> agents, CancellationToken cancellationToken = default)
    {
        var fingerprints = _fingerprints ??= new Dictionary<string, string>(_catalog.Fingerprints(), StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var unchanged = 0;
        var failed = 0;
        var metadataOnly = 0;
        var warnings = new List<string>();
        var unlisted = new List<(AgentDefinition, string)>();
        void Report(string source, Exception exception)
        {
            failed++;
            if (warnings.Count < 100) warnings.Add(source + ": " + exception.Message);
        }
        foreach (var agent in agents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // One transaction for the agent's sources, committed every CommitEvery conversations. A fingerprint reaches the
                // in-memory copy only with the commit that made it durable, so a refresh cut short reads those sources again.
                using var batch = _catalog.BeginBatch();
                var pending = new List<(string Source, string Fingerprint)>();
                var written = 0;
                var open = System.Diagnostics.Stopwatch.StartNew();
                void Commit()
                {
                    batch.Commit();
                    foreach (var (source, fingerprint) in pending) fingerprints[source] = fingerprint;
                    pending.Clear();
                    written = 0;
                    open.Restart();
                }
                void Wrote(string source, string fingerprint)
                {
                    batch.SetFingerprint(source, fingerprint);
                    pending.Add((source, fingerprint));
                    if (++written >= CommitEvery || open.Elapsed >= CommitAfter) Commit();
                }
                if (agent.Id is "hermes" or "opencode")
                {
                    var hermes = agent.Id == "hermes";
                    var database = Path.Combine(agent.HistoryDirectory, hermes ? "state.db" : OpenCodeHistoryReader.DatabaseName);
                    if (!File.Exists(database)) continue;
                    // The database as a whole first: untouched since the last import, nothing in it is read. Changed (Hermes writes
                    // to it while idle), each session is listed with a fingerprint of its row and its messages, and only the
                    // sessions whose fingerprint moved have their messages read; the fingerprints are kept per session.
                    var fingerprint = Fingerprint(database) + "|" + Fingerprint(database + "-wal");
                    var source = agent.Id + "|" + database;
                    if (fingerprints.GetValueOrDefault(source) == fingerprint) { unchanged++; continue; }
                    string? Known(string sessionId) =>
                        fingerprints.GetValueOrDefault(source + "#" + sessionId) is { } kept && kept.StartsWith(ImportVersion + ":", StringComparison.Ordinal) ? kept[(ImportVersion.ToString().Length + 1)..] : null;
                    var sessions = hermes ? HermesHistoryReader.Read(agent, Known) : OpenCodeHistoryReader.Read(agent, Known);
                    foreach (var (conversation, sessionFingerprint) in sessions.Imported)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        batch.Upsert(conversation);
                        Wrote(source + "#" + conversation.SessionId, ImportVersion + ":" + sessionFingerprint);
                        imported++;
                    }
                    unchanged += sessions.Unchanged;
                    foreach (var id in sessions.Unlisted ?? []) unlisted.Add((agent, id));
                    Wrote(source, fingerprint);
                    Commit();
                    continue;
                }
                // The transcripts are parsed on several threads and written here, on this one, in the order they finish: a refresh
                // that finds several active transcripts changed (the day's sessions, tens of megabytes each) takes about the
                // longest parse instead of their sum. An enumeration that fails part-way still commits what was written before it.
                Exception? stopped = null;
                // The lister reads the fingerprints on its own thread while this one records new ones, so it gets a copy as they stand now.
                var knownAtStart = new Dictionary<string, string>(fingerprints, StringComparer.OrdinalIgnoreCase);
                foreach (var result in new TranscriptParser(agent, _fullTextLimit, knownAtStart, cancellationToken).Results())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (result)
                    {
                        case TranscriptParser.Unchanged: unchanged++; break;
                        case TranscriptParser.Failed failedFile: Report(failedFile.Path, failedFile.Error); break;
                        case TranscriptParser.Stopped stop: stopped = stop.Error; break;
                        case TranscriptParser.Parsed parsed:
                            batch.Upsert(parsed.Conversation);
                            if (parsed.MetadataOnly) metadataOnly++; else imported++;
                            Wrote(parsed.Source, parsed.Fingerprint);
                            break;
                    }
                }
                Commit();
                if (stopped is not null) throw stopped;
            }
            catch (Exception exception) when (IsSourceError(exception)) { Report(agent.HistoryDirectory, exception); }
        }
        return new IndexResult(imported, unchanged, failed, warnings, metadataOnly, unlisted);
    }

    /// <summary>
    /// Lists an agent's transcripts and parses the changed ones on a few threads at once, handing each result to the caller's
    /// thread as it finishes. At most <see cref="Parallelism"/> parses run together, and a parse waits for the writer while
    /// <see cref="BufferedResults"/> results stand unwritten, so a first import of hundreds of transcripts never holds more than a
    /// few of them in memory. Cancelling stops the listing, the parses and the hand-over alike.
    /// </summary>
    private sealed class TranscriptParser(AgentDefinition agent, long fullTextLimit, IReadOnlyDictionary<string, string> known, CancellationToken cancellationToken)
    {
        /// <summary>Parses at once: the cores less one for the rest of the app, and no more than the active transcripts there usually are.</summary>
        internal static readonly int Parallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 6);
        private static readonly int BufferedResults = Parallelism * 2;

        internal abstract record Result;
        internal sealed record Unchanged : Result;
        internal sealed record Parsed(Conversation Conversation, bool MetadataOnly, string Source, string Fingerprint) : Result;
        internal sealed record Failed(string Path, Exception Error) : Result;
        /// <summary>The listing itself failed (a folder that cannot be read, the 10,000-file limit): what was listed before stands.</summary>
        internal sealed record Stopped(Exception Error) : Result;

        internal IEnumerable<Result> Results()
        {
            using var results = new BlockingCollection<Result>(BufferedResults);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var producer = Task.Run(() => ProduceAsync(results, stop.Token), CancellationToken.None);
            try
            {
                foreach (var result in results.GetConsumingEnumerable(cancellationToken)) yield return result;
            }
            finally
            {
                // The consumer left early (cancelled, or a write failed): the parses still running are let go and waited for, so
                // none is left adding to a collection nobody reads.
                stop.Cancel();
                try { producer.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
            }
        }

        private async Task ProduceAsync(BlockingCollection<Result> results, CancellationToken token)
        {
            using var slots = new SemaphoreSlim(Parallelism);
            var parses = new List<Task>();
            try
            {
                var count = 0;
                foreach (var file in SourceFiles(agent))
                {
                    token.ThrowIfCancellationRequested();
                    if (++count > 10000) throw new InvalidDataException("Indexing stopped at the 10,000-file safety limit for this source.");
                    string fingerprint;
                    var source = agent.Id + "|" + file.FullName;
                    // A file gone or unreadable since it was listed is one failure among the rest, not the end of the listing.
                    try { fingerprint = Fingerprint(file); }
                    catch (Exception exception) when (IsSourceError(exception)) { results.Add(new Failed(file.FullName, exception), token); continue; }
                    if (known.GetValueOrDefault(source) == fingerprint) { results.Add(new Unchanged(), token); continue; }
                    await slots.WaitAsync(token).ConfigureAwait(false);
                    parses.Add(Task.Run(() =>
                    {
                        try { results.Add(Parse(file, source, fingerprint, token), token); }
                        finally { slots.Release(); }
                    }, token));
                }
            }
            catch (Exception exception) when (IsSourceError(exception) && !token.IsCancellationRequested)
            {
                try { results.Add(new Stopped(exception), token); } catch (OperationCanceledException) { }
            }
            finally
            {
                try { await Task.WhenAll(parses).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                results.CompleteAdding();
            }
        }

        private Result Parse(FileInfo file, string source, string fingerprint, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (file.Length > fullTextLimit)
                {
                    var note = $"Transcript is {file.Length / 1048576.0:N1} MiB, above the {fullTextLimit / 1048576.0:N0} MiB full-text limit. Identity, timing and the first message were imported; its text is not searchable.";
                    return new Parsed(TranscriptReader.ReadMetadata(agent, file.FullName, note), MetadataOnly: true, source, fingerprint);
                }
                return new Parsed(TranscriptReader.ReadJsonLines(agent, file.FullName), MetadataOnly: false, source, fingerprint);
            }
            catch (Exception exception) when (IsSourceError(exception)) { return new Failed(file.FullName, exception); }
        }
    }

    private static bool IsSourceError(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException or ArgumentException or OverflowException;

    private static string Fingerprint(string path) => Fingerprint(new FileInfo(path));

    private static string Fingerprint(FileInfo file) =>
        ImportVersion + ":" + (file.Exists ? file.Length + ":" + file.LastWriteTimeUtc.Ticks : "missing");

    /// <summary>The agent's transcripts, as the listing describes them: a <see cref="FileInfo"/> from the listing already carries the size and time the fingerprint needs, so no file is asked again.</summary>
    private static IEnumerable<FileInfo> SourceFiles(AgentDefinition agent)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            IgnoreInaccessible = false, RecurseSubdirectories = agent.Id == "codex"
        };
        if (agent.Id == "claude")
        {
            var projects = Path.Combine(agent.HistoryDirectory, "projects");
            if (!Directory.Exists(projects)) yield break;
            foreach (var project in new DirectoryInfo(projects).EnumerateDirectories("*", options))
                foreach (var file in project.EnumerateFiles("*.jsonl", options)) yield return file;
        }
        else if (agent.Id == "codex")
        {
            var sessions = Path.Combine(agent.HistoryDirectory, "sessions");
            if (!Directory.Exists(sessions)) yield break;
            foreach (var file in new DirectoryInfo(sessions).EnumerateFiles("rollout-*.jsonl", options)) yield return file;
        }
    }
}
