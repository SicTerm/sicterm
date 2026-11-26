using System.Xml.Linq;
using Sic.Core.Agents;
using Windows.Management.Deployment;

namespace Sic.App;

/// <summary>
/// The Start-tile logo of an app installed from the Store, read from its package's manifest (docs/decisions/agent-icons.md):
/// the Codex desktop app's, for Codex's tile.
/// </summary>
internal static class StoreAppLogos
{
    /// <summary>
    /// The Square44x44Logo, its unplated variant and the background colour that the package's manifest names for its app in the
    /// Start menu, for the current user; null when the package is not installed or names none. A logo the package holds only in scaled variants
    /// (Square44x44Logo.scale-200.png) is the largest of them. Reads the package list, so it is slow: not on the UI thread.
    /// </summary>
    internal static PackagedAppLogo? Find(string packageName)
    {
        try
        {
            var package = new PackageManager().FindPackagesForUser("")
                .FirstOrDefault(candidate => string.Equals(candidate.Id.Name, packageName, StringComparison.OrdinalIgnoreCase));
            if (package?.InstalledLocation?.Path is not { } root) return null;
            var manifest = XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
            var visual = manifest.Descendants().Where(element => element.Name.LocalName == "VisualElements")
                .FirstOrDefault(element => !string.Equals((string?)element.Attribute("AppListEntry"), "none", StringComparison.OrdinalIgnoreCase));
            if ((string?)visual?.Attribute("Square44x44Logo") is not { Length: > 0 } logo) return null;
            var named = Path.Combine(root, logo.Replace('/', Path.DirectorySeparatorChar));
            return Resolve(named) is { } file
                ? new PackagedAppLogo(file, (string?)visual!.Attribute("BackgroundColor"), Unplated(named))
                : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Diagnostics.Log("store app " + packageName + ": " + exception.Message);
            return null;
        }
    }

    /// <summary>
    /// The logo's largest variant for a dark taskbar (Square44x44Logo.targetsize-256_altform-unplated.png): the art alone, white,
    /// with no tile. Not the lightunplated variant, which is black for a light taskbar. Null when the package holds none.
    /// </summary>
    private static string? Unplated(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) return null;
        const string suffix = "_altform-unplated";
        return Directory.GetFiles(directory, Path.GetFileNameWithoutExtension(path) + ".targetsize-*" + suffix + Path.GetExtension(path))
            .Select(file => (File: file, Size: int.TryParse(Path.GetFileNameWithoutExtension(file).Split(".targetsize-").Last().Replace(suffix, ""), out var size) ? size : 0))
            .OrderByDescending(variant => variant.Size).Select(variant => variant.File).FirstOrDefault();
    }

    private static string? Resolve(string path)
    {
        if (File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) return null;
        var scaled = Directory.GetFiles(directory, Path.GetFileNameWithoutExtension(path) + ".scale-*" + Path.GetExtension(path));
        return scaled.OrderByDescending(file => int.TryParse(Path.GetFileNameWithoutExtension(file).Split(".scale-").Last(), out var scale) ? scale : 0).FirstOrDefault();
    }
}
