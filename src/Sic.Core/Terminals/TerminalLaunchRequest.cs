namespace Sic.Core.Terminals;

/// <param name="Environment">
/// Variables set in the child on top of this process's own environment; a null value takes that variable out of what the
/// child inherits instead.
/// </param>
public sealed record TerminalLaunchRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int Columns = 120,
    int Rows = 30,
    IReadOnlyDictionary<string, string?>? Environment = null);
