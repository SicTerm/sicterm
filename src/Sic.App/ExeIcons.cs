using System.Runtime.InteropServices;

namespace Sic.App;

/// <summary>
/// An exe's icon at exactly a frame's size, from its fullest icon group (docs/decisions/agent-icons.md). The shell's picture of
/// an exe comes from its first group, and Claude Code's claude.exe fills that one (IDI_MYICON) with a single 256 px image, beside
/// a second group of fourteen frames drawn for 16 to 256 px: shrinking the 256 px image makes a soft 32 px tile, where reading
/// the group with the most frames at the tile's size takes the frame drawn for it.
/// </summary>
internal static class ExeIcons
{
    /// <summary>The exe's icon at <paramref name="size"/> px as straight-alpha BGRA rows, top row first; null when it has none.</summary>
    internal static byte[]? Read(string path, int size)
    {
        var index = FullestGroup(path);
        if (index < 0) return null;
        var icons = new nint[1];
        var ids = new uint[1];
        var extracted = PrivateExtractIcons(path, index, size, size, icons, ids, 1, 0);
        if (extracted is 0 or uint.MaxValue || icons[0] == 0) return null;
        try { return Pixels(icons[0], size); }
        finally { DestroyIcon(icons[0]); }
    }

    /// <summary>
    /// The icon an icon string names (<c>path,-id</c>, as File Explorer's menu takes it) at <paramref name="size"/> px, read the
    /// way the shell reads it; null when the file or the resource is not there. For verification.
    /// </summary>
    internal static byte[]? ReadReference(string reference, int size)
    {
        var comma = reference.LastIndexOf(',');
        if (comma < 0 || !int.TryParse(reference[(comma + 1)..], out var index)) return null;
        var icons = new nint[1];
        var ids = new uint[1];
        var extracted = PrivateExtractIcons(reference[..comma], index, size, size, icons, ids, 1, 0);
        if (extracted is 0 or uint.MaxValue || icons[0] == 0) return null;
        try { return Pixels(icons[0], size); }
        finally { DestroyIcon(icons[0]); }
    }

    /// <summary>The index, in the order the shell counts them, of the exe's icon group with the most frames; the first on a tie, -1 when it has none.</summary>
    private static int FullestGroup(string path)
    {
        var module = LoadLibraryEx(path, 0, LoadAsDataFile | LoadAsImageResource);
        if (module == 0) return -1;
        try
        {
            var counts = new List<int>();
            // A group's name is only valid during the callback, so its frames are counted there.
            EnumResNameProc count = (handle, _, name, _) =>
            {
                var resource = FindResource(handle, name, GroupIcon);
                var data = resource == 0 ? 0 : LockResource(LoadResource(handle, resource));
                counts.Add(data != 0 && SizeofResource(handle, resource) >= 6 ? (ushort)Marshal.ReadInt16(data, 4) : 0);
                return true;
            };
            EnumResourceNames(module, GroupIcon, count, 0);
            GC.KeepAlive(count);
            if (counts.Count == 0 || counts.Max() == 0) return -1;
            return counts.IndexOf(counts.Max());
        }
        finally { FreeLibrary(module); }
    }

    /// <summary>The icon's colour bitmap as top-down BGRA; an icon without alpha takes it from its mask.</summary>
    private static byte[]? Pixels(nint icon, int size)
    {
        if (!GetIconInfo(icon, out var info)) return null;
        var screen = GetDC(0);
        try
        {
            var pixels = Bits(screen, info.Color, size);
            if (pixels is null) return null;
            var hasAlpha = false;
            for (var index = 3; index < pixels.Length && !hasAlpha; index += 4) hasAlpha = pixels[index] != 0;
            if (!hasAlpha && Bits(screen, info.Mask, size) is { } mask)
                for (var index = 3; index < pixels.Length; index += 4) pixels[index] = mask[index - 1] == 0 ? (byte)255 : (byte)0;
            return pixels;
        }
        finally
        {
            ReleaseDC(0, screen);
            if (info.Color != 0) DeleteObject(info.Color);
            if (info.Mask != 0) DeleteObject(info.Mask);
        }
    }

    private static byte[]? Bits(nint dc, nint bitmap, int size)
    {
        if (bitmap == 0) return null;
        var header = new BitmapInfoHeader { Size = 40, Width = size, Height = -size, Planes = 1, BitCount = 32 };
        var pixels = new byte[size * size * 4];
        return GetDIBits(dc, bitmap, 0, (uint)size, pixels, ref header, 0) == size ? pixels : null;
    }

    private const uint LoadAsDataFile = 0x2, LoadAsImageResource = 0x20;
    private static readonly nint GroupIcon = 14;

    private delegate bool EnumResNameProc(nint module, nint type, nint name, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { internal int IsIcon, HotspotX, HotspotY; internal nint Mask, Color; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
        internal uint Compression, SizeImage; internal int XPelsPerMeter, YPelsPerMeter; internal uint ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PrivateExtractIcons(string file, int index, int width, int height, nint[] icons, uint[] ids, uint count, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint LoadLibraryEx(string file, nint reserved, uint flags);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(nint module);
    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumResourceNames(nint module, nint type, EnumResNameProc callback, nint parameter);
    [DllImport("kernel32.dll", EntryPoint = "FindResourceW", ExactSpelling = true)] private static extern nint FindResource(nint module, nint name, nint type);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LockResource(nint data);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint module, nint resource);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
}
