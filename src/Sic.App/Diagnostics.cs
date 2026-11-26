namespace Sic.App;

/// <summary>
/// A plain log in the data folder (<c>sic.log</c>, rolled to <c>sic.log.1</c> past a megabyte): launches and hand-offs, quits
/// and what asked for them, and any exception that ends SIC, which otherwise exits without a word when a managed exception
/// reaches the XAML handler (a crash on October 7, 2026, after an Open in SIC item was chosen, left nothing to read). Written
/// synchronously and rarely; a line that cannot be written is dropped.
/// </summary>
internal static class Diagnostics
{
    private const long RollAt = 1_000_000;
    private static readonly object Gate = new();
    private static string? _path;

    internal static string FileName => "sic.log";

    internal static void Start(string dataDirectory) => _path = Path.Combine(dataDirectory, FileName);

    internal static void Log(string message)
    {
        if (_path is null) return;
        lock (Gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > RollAt) File.Move(_path, _path + ".1", overwrite: true);
                File.AppendAllText(_path, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + " [" + Environment.ProcessId + "] " + message + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
