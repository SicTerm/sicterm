using System.Diagnostics;
using Sic.Core.Sessions;
using Xunit;

namespace Sic.Core.Tests;

public sealed class FolderIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sic-folder-identity-" + Guid.NewGuid().ToString("N"));
    public FolderIdentityTests() => Directory.CreateDirectory(_root);
    // The junction goes first, on its own: a recursive delete of the folder holding one is refused.
    public void Dispose()
    {
        if (Directory.Exists(Path.Combine(_root, "link"))) Directory.Delete(Path.Combine(_root, "link"));
        Directory.Delete(_root, true);
    }

    [Fact]
    public void Same_TakesAFolderHoweverItIsWritten()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        Assert.True(FolderIdentity.Same(folder, folder.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.True(FolderIdentity.Same(folder, Path.Combine(_root, "app", "..", "app")));
        Assert.False(FolderIdentity.Same(folder, Directory.CreateDirectory(Path.Combine(_root, "other")).FullName));
        Assert.False(FolderIdentity.Same(folder, Path.Combine(_root, "missing")));
        Assert.False(FolderIdentity.Same(folder, null));
        Assert.False(FolderIdentity.Same("", ""));
    }

    [Fact]
    public void Same_FollowsAJunctionToTheFolderHermesRecords()
    {
        var real = Directory.CreateDirectory(Path.Combine(_root, "real", "app")).FullName;
        var link = Path.Combine(_root, "link");
        // A junction, unlike a symbolic link, needs no elevation.
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{Path.Combine(_root, "real")}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true }))
            mklink!.WaitForExit();
        var through = Path.Combine(link, "app");
        Assert.True(Directory.Exists(through));
        Assert.True(FolderIdentity.Same(through, real));
        // The folder the junction leads to, as a plain path: no \\?\ prefix, which Windows returns the final path with.
        var resolved = FolderIdentity.Resolved(through);
        Assert.Equal(FolderIdentity.Resolved(real), resolved);
        Assert.StartsWith(Path.GetPathRoot(real)!, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("real", "app"), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.Null(FolderIdentity.Resolved(Path.Combine(_root, "missing")));
    }
}
