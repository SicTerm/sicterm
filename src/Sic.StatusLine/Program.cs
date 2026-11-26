using Sic.Core.Limits;

// Claude Code's status line in the panes SIC starts: the session's JSON on standard input, the plan's usage limits kept in the
// data folder, and the user's own status line, if any, run with the same input and printed. See ClaudeStatusLine.Run.
return ClaudeStatusLine.Run(args, Console.OpenStandardInput(), Console.OpenStandardOutput(),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperIntelligentChat"));
