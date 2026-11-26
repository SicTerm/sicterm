using System.Net;
using Velopack;
using Velopack.Sources;
using Sic.Core.Shell;

namespace Sic.App;

/// <summary>
/// Updates for a copy installed with SIC's Setup (Velopack). Releases are read from the public repository's GitHub releases,
/// with no token; a folder or web address in <see cref="SourceVariable"/> stands in for GitHub, to try a release before it is
/// published. Until November 2026 the repository was private and each PC read it with a token of its own. A newer release downloads in the
/// background and is applied once SIC has quit, so it never lands under running agents; one that an earlier run downloaded but
/// did not get to apply is applied as the next launch starts, before the window opens. A copy run from a build folder is not
/// installed and never updates itself.
/// </summary>
internal sealed class Updates
{
    internal const string Repository = "https://github.com/SicTerm/sicterm";
    internal const string SourceVariable = "SIC_UPDATE_SOURCE";
    private readonly UpdateManager _installed = new(Source());

    internal bool IsInstalled => _installed.IsInstalled;
    /// <summary>
    /// Installed for every user of the PC by SicTerm's MSI, in a folder this user cannot change (Program Files): Velopack's updater
    /// cannot update it there, so it neither checks nor downloads, and the next MSI updates it (docs/decisions/installer.md).
    /// </summary>
    internal bool IsManaged => _managed ??= IsInstalled && !InstallFolder.IsWritable(InstallRoot);
    private bool? _managed;
    /// <summary>Installed by SicTerm's Setup, or by the MSI for this user alone: it checks for releases and updates itself.</summary>
    internal bool UpdatesItself => IsInstalled && !IsManaged;
    /// <summary>The installed copy's root, which holds Update.exe beside <c>current\</c>, where SicTerm.exe runs (Velopack's layout).</summary>
    internal static string InstallRoot => Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? AppContext.BaseDirectory;
    internal string? Version => _installed.CurrentVersion?.ToString();
    /// <summary>Where releases are read from: the GitHub repository, or what <see cref="SourceVariable"/> names instead.</summary>
    internal static string Origin => Environment.GetEnvironmentVariable(SourceVariable) is { Length: > 0 } configured ? configured.Trim() : Repository;
    /// <summary>The release downloaded and waiting for SIC to quit; null when there is none.</summary>
    internal VelopackAsset? Ready { get; private set; }

    private static IUpdateSource Source()
    {
        if (Origin == Repository) return new GithubSource(Repository, "", prerelease: false);
        return Uri.TryCreate(Origin, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? new SimpleWebSource(uri) : new SimpleFileSource(new DirectoryInfo(Origin));
    }

    /// <summary>
    /// Looks for a newer release and downloads it. Returns the version downloaded, or null when this is the newest. A release
    /// already downloaded is not fetched again.
    /// </summary>
    internal async Task<string?> CheckAsync(CancellationToken cancellation)
    {
        var manager = new UpdateManager(Source());
        var update = await manager.CheckForUpdatesAsync();
        if (update is null) return null;
        if (Ready?.Version != update.TargetFullRelease.Version)
        {
            await manager.DownloadUpdatesAsync(update, null, cancellation);
            Ready = update.TargetFullRelease;
        }
        return Ready!.Version.ToString();
    }

    /// <summary>
    /// Hands the downloaded release to Velopack's updater, which waits for this process to exit, applies it, and restarts SIC
    /// when <paramref name="restart"/> says so. Called as SIC quits; does nothing when nothing is waiting.
    /// </summary>
    internal void ApplyOnExit(bool restart)
    {
        if (Ready is { } release) _installed.WaitExitThenApplyUpdates(release, silent: !restart, restart: restart, restartArgs: null);
    }

    /// <summary>
    /// At launch, once this process owns the data folder: applies a release an earlier run downloaded but did not get to apply,
    /// and restarts into it with the same arguments. False, having done nothing, when there is none, when this copy is not
    /// installed, or when the updater cannot be reached; a launch is never stopped by its updates.
    /// </summary>
    internal static bool ApplyPendingAndRestart(string[] arguments)
    {
        try
        {
            var manager = new UpdateManager(Source());
            if (!manager.IsInstalled || manager.UpdatePendingRestart is not { } pending || !InstallFolder.IsWritable(InstallRoot)) return false;
            manager.ApplyUpdatesAndRestart(pending, arguments);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    /// <summary>What a failed check says in Settings: GitHub's hourly limit for requests without a sign-in named as such, anything else as it came.</summary>
    internal static string Describe(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests }
            => "GitHub turned the check away for now (its hourly limit); the next check tries again.",
        HttpRequestException => "The releases could not be reached: " + exception.Message,
        _ => exception.Message
    };
}
