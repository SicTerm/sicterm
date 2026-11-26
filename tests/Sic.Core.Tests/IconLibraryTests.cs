using System.Runtime.InteropServices;
using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class IconLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sic-icon-library-tests-" + Guid.NewGuid().ToString("N"));
    public IconLibraryTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    // Red over blue, opaque, so the rows' order shows.
    private static IconImage Frame(int size, byte alpha = 255)
    {
        var pixels = new byte[size * size * 4];
        for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
            {
                var at = (row * size + column) * 4;
                if (row < size / 2) pixels[at + 2] = 255; else pixels[at] = 255;
                pixels[at + 3] = alpha;
            }
        return new IconImage(size, pixels);
    }

    private string Write(byte[] library)
    {
        var path = Path.Combine(_root, IconLibrary.FileName(library));
        File.WriteAllBytes(path, library);
        return path;
    }

    [Fact]
    public void WindowsFindsEachIconByItsResourceIdAsTheMenuNamesIt()
    {
        var path = Write(IconLibrary.Build([
            new IconGroup(102, [Frame(16), Frame(32)]),
            new IconGroup(103, [Frame(16), Frame(20), Frame(24), Frame(32), Frame(48)])]));
        // "path,-103", as the menu's icon string reads, is the group with resource id 103: what ExtractIconEx takes as index -103.
        foreach (var id in new[] { 102, 103 })
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            // One large and one small.
            Assert.Equal(2u, ExtractIconEx(path, -id, large, small, 1));
            Assert.NotEqual(IntPtr.Zero, large[0]);
            Assert.NotEqual(IntPtr.Zero, small[0]);
            DestroyIcon(large[0]);
            DestroyIcon(small[0]);
        }
        Assert.Equal(0u, ExtractIconEx(path, -104, new IntPtr[1], null, 1));
    }

    [Fact]
    public void AFrameKeepsItsPixelsTopRowFirst()
    {
        var path = Write(IconLibrary.Build([new IconGroup(103, [Frame(16), Frame(32), Frame(48)])]));
        var icons = new IntPtr[1];
        var ids = new uint[1];
        Assert.Equal(1u, PrivateExtractIcons(path, -103, 32, 32, icons, ids, 1, 0));
        try
        {
            Assert.True(GetIconInfo(icons[0], out var info));
            try
            {
                var pixels = new byte[32 * 32 * 4];
                Assert.Equal(pixels.Length, GetBitmapBits(info.Color, pixels.Length, pixels));
                // Top-left is red, bottom-left blue: BGRA.
                Assert.Equal(new byte[] { 0, 0, 255 }, pixels[..3]);
                Assert.Equal(new byte[] { 255, 0, 0 }, pixels[(31 * 32 * 4)..(31 * 32 * 4 + 3)]);
            }
            finally { DeleteObject(info.Color); DeleteObject(info.Mask); }
        }
        finally { DestroyIcon(icons[0]); }
    }

    [Fact]
    public void TheNameFollowsTheContent()
    {
        var one = IconLibrary.Build([new IconGroup(103, [Frame(16)])]);
        var other = IconLibrary.Build([new IconGroup(103, [Frame(16, alpha: 128)])]);
        Assert.Equal(IconLibrary.FileName(one), IconLibrary.FileName(IconLibrary.Build([new IconGroup(103, [Frame(16)])])));
        Assert.NotEqual(IconLibrary.FileName(one), IconLibrary.FileName(other));
        Assert.Matches("^menu-icons-[0-9a-f]{12}\\.dll$", IconLibrary.FileName(one));
    }

    [Fact]
    public void ABadLibraryIsRefused()
    {
        Assert.Throws<ArgumentException>(() => IconLibrary.Build([]));
        Assert.Throws<ArgumentException>(() => IconLibrary.Build([new IconGroup(103, [])]));
        Assert.Throws<ArgumentException>(() => IconLibrary.Build([new IconGroup(103, [Frame(16)]), new IconGroup(103, [Frame(32)])]));
        Assert.Throws<ArgumentException>(() => IconLibrary.Build([new IconGroup(103, [Frame(16), Frame(16)])]));
        Assert.Throws<ArgumentException>(() => IconLibrary.Build([new IconGroup(103, [new IconImage(16, new byte[10])])]));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { public bool IsIcon; public int HotspotX; public int HotspotY; public IntPtr Mask; public IntPtr Color; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int width, int height, IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("gdi32.dll")]
    private static extern int GetBitmapBits(IntPtr bitmap, int count, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
