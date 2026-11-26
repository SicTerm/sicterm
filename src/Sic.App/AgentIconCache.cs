using System.Runtime.InteropServices.WindowsRuntime;
using Sic.Core.Agents;
using Sic.Core.Shell;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Sic.App;

/// <summary>
/// The agents' own icons as SIC draws them (docs/decisions/agent-icons.md): each one's 32 and 48 px frames, read from its source
/// (<see cref="AgentIcons.Source"/>) and saved as PNG under the data folder, so a launch shows them before detection has run.
/// A stamp beside each pair names the source, its size and time, how it was read and the colour of the tile it sits on, so a
/// tool that updates is read again and an unchanged one is not.
/// </summary>
internal sealed class AgentIconCache(string directory)
{
    internal static readonly int[] Frames = [32, 48];
    private readonly Dictionary<string, AgentIconSource?> _cached = new(StringComparer.Ordinal);

    /// <summary>Which frames are on disk, as last looked: every tile asks, and a list of hundreds of rows asks hundreds of times. Forgotten whenever the cache writes or removes frames.</summary>
    private readonly Dictionary<string, bool> _framesOnDisk = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cached frame for a tile of this size (the 32 px frame at 16 px and under, the 48 above), or null.</summary>
    internal string? FramePath(string id, double size)
    {
        var path = Frame(id, size <= 16 ? 32 : 48);
        if (!_framesOnDisk.TryGetValue(path, out var exists)) _framesOnDisk[path] = exists = File.Exists(path);
        return exists ? path : null;
    }

    /// <summary>The source the cached frames were read from, with its tile colour, while that file is still there; null when nothing is cached.</summary>
    internal AgentIconSource? CachedSource(string id)
    {
        if (_cached.TryGetValue(id, out var known)) return known;
        AgentIconSource? source = null;
        try
        {
            var fields = File.Exists(Stamp(id)) ? File.ReadAllText(Stamp(id)).Split('|') : [];
            if (fields.Length > 0 && File.Exists(fields[0]) && Frames.All(frame => File.Exists(Frame(id, frame))))
                source = new(fields[0], fields.Length > 4 && fields[4].Length > 0 ? fields[4] : null);
        }
        catch (IOException) { }
        return _cached[id] = source;
    }

    /// <summary>
    /// Brings the agent's cached frames in line with <paramref name="source"/>: read again when the source, its file or its tile
    /// colour changed, removed when there is none. True when the frames changed. A source that cannot be read leaves nothing
    /// cached, so the tile falls back to the letter rather than to an empty picture.
    /// </summary>
    internal async Task<bool> UpdateAsync(string id, AgentIconSource? source)
    {
        _cached.Remove(id);
        _framesOnDisk.Clear();
        if (source is null || !File.Exists(source.Path)) return Remove(id);
        var info = new FileInfo(source.Path);
        // The fourth field is how the frames were read, so a cache from an earlier way is read again.
        var stamp = string.Join('|', source.Path, info.Length, info.LastWriteTimeUtc.Ticks, ReadVersion, source.Plate ?? "");
        if (File.Exists(Stamp(id)) && File.ReadAllText(Stamp(id)) == stamp && Frames.All(frame => File.Exists(Frame(id, frame)))) return false;
        Remove(id);
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var frame in Frames) await WriteFrameAsync(source.Path, Frame(id, frame), frame);
            await File.WriteAllTextAsync(Stamp(id), stamp);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Diagnostics.Log("agent icon " + id + " from " + source.Path + ": " + exception.Message);
            Remove(id);
        }
        _cached.Remove(id);
        return true;
    }

    /// <summary>
    /// How frames are read. 2 (October 12, 2026): an exe's from its fullest icon group at their own sizes (<see cref="ExeIcons"/>).
    /// 3: a .png decoded from the file itself, which a Store package's folder allows where the shell's picture of it may not.
    /// </summary>
    private const string ReadVersion = "3";

    /// <summary>
    /// One frame as PNG: an exe's frame drawn for this size, from its fullest icon group; a .png's own pixels, scaled; otherwise
    /// the shell's picture of the file, which for an .ico is the frame nearest the size.
    /// </summary>
    private static async Task WriteFrameAsync(string source, string path, int frame)
    {
        var exePixels = source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ExeIcons.Read(source, frame) : null;
        using var bitmap = exePixels is not null
            ? SoftwareBitmap.CreateCopyFromBuffer(exePixels.AsBuffer(), BitmapPixelFormat.Bgra8, frame, frame, BitmapAlphaMode.Straight)
            : source.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? await DecodeAsync(source)
            : await ThumbnailAsync(await StorageFile.GetFileFromPathAsync(source), frame);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        if (bitmap.PixelWidth != frame || bitmap.PixelHeight != frame)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)frame;
            encoder.BitmapTransform.ScaledHeight = (uint)frame;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        await encoder.FlushAsync();
        var bytes = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static async Task<SoftwareBitmap> DecodeAsync(string source)
    {
        await using var file = File.OpenRead(source);
        var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private static async Task<SoftwareBitmap> ThumbnailAsync(StorageFile file, int frame)
    {
        using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)frame, ThumbnailOptions.ResizeThumbnail)
            ?? throw new InvalidOperationException("The shell has no picture of the file.");
        var decoder = await BitmapDecoder.CreateAsync(thumbnail);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>The sizes File Explorer's menu asks for, from 100 % to 200 % scaling, each drawn on its own pixel grid.</summary>
    internal static readonly int[] MenuFrames = [16, 20, 24, 32, 48];
    private const string MenuIndexFile = "menu-icons.txt";
    private const string MenuLibraryPattern = "menu-icons-*.dll";

    /// <summary>The agent's icon in the library (<c>path,-id</c>, <see cref="IconLibrary"/>), or null when it has none there.</summary>
    internal string? MenuIcon(string id)
    {
        try
        {
            var lines = File.ReadAllLines(Path.Combine(directory, MenuIndexFile));
            if (lines.Length == 0 || !File.Exists(Path.Combine(directory, lines[0]))) return null;
            return lines.Skip(1).Select(line => line.Split('=')).FirstOrDefault(parts => parts.Length == 2 && parts[0] == id) is { } entry
                ? Path.Combine(directory, lines[0]) + ",-" + entry[1] : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Brings the icon library for File Explorer's menu in line with the cache: every agent among <paramref name="ids"/> whose tile
    /// is its own icon from something other than an exe (an exe the menu names itself) goes in under the command DLL's id for it,
    /// drawn as the tile draws it, Start-tile art on its plate. The library is named by its content and listed in an index beside
    /// it; libraries from before are removed when nothing holds them. Never throws: without it the menu shows the letter.
    /// </summary>
    internal async Task UpdateMenuLibraryAsync(IEnumerable<string> ids)
    {
        try
        {
            var groups = new List<IconGroup>();
            var index = new List<string>();
            foreach (var id in ids)
            {
                if (CachedSource(id) is not { } source || source.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || !ExplorerMenu.CommandIconIds.TryGetValue(id + ".ico", out var groupId)) continue;
                var frames = new List<IconImage>();
                foreach (var size in MenuFrames) frames.Add(new IconImage(size, await MenuFrameAsync(Frame(id, 48), size, source.Plate)));
                groups.Add(new IconGroup(groupId, frames));
                index.Add(id + "=" + groupId);
            }
            var indexPath = Path.Combine(directory, MenuIndexFile);
            string? keep = null;
            if (groups.Count == 0) File.Delete(indexPath);
            else
            {
                var library = IconLibrary.Build(groups);
                keep = IconLibrary.FileName(library);
                var path = Path.Combine(directory, keep);
                if (!File.Exists(path))
                {
                    var temporary = path + "." + Environment.ProcessId + ".tmp";
                    await File.WriteAllBytesAsync(temporary, library);
                    File.Move(temporary, path, overwrite: true);
                }
                var text = string.Join('\n', index.Prepend(keep));
                if (!File.Exists(indexPath) || await File.ReadAllTextAsync(indexPath) != text) await File.WriteAllTextAsync(indexPath, text);
            }
            // Explorer may still have an older one open; it goes on a later pass.
            foreach (var old in Directory.EnumerateFiles(directory, MenuLibraryPattern).Where(file => !string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(old); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Diagnostics.Log("menu icons: " + exception.Message); }
    }

    /// <summary>
    /// One menu frame from the cached 48 px frame, as BGRA with straight alpha: the art at the frame's size, or Start-tile art
    /// inset on its plate, a rounded square of the tile's colour with the corners of SIC's letter tiles (a fifth of the size, 3 px
    /// at least, scripts\Build-AgentIcons.ps1), as the app's tile draws it.
    /// </summary>
    private static async Task<byte[]> MenuFrameAsync(string cached, int size, string? plate)
    {
        var art = plate is null ? size : (int)Math.Round(size * 0.76);
        byte[] artPixels;
        await using (var file = File.OpenRead(cached))
        {
            var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
            var transform = new BitmapTransform { ScaledWidth = (uint)art, ScaledHeight = (uint)art, InterpolationMode = BitmapInterpolationMode.Fant };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            artPixels = data.DetachPixelData();
        }
        // Composed premultiplied, then made straight for the icon.
        var pixels = new byte[size * size * 4];
        if (plate is not null)
        {
            var color = Convert.FromHexString(plate);
            var radius = Math.Max(3.0, size / 5.0);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var cover = Coverage(x, y, size, radius);
                    var at = (y * size + x) * 4;
                    pixels[at] = (byte)Math.Round(color[2] * cover);
                    pixels[at + 1] = (byte)Math.Round(color[1] * cover);
                    pixels[at + 2] = (byte)Math.Round(color[0] * cover);
                    pixels[at + 3] = (byte)Math.Round(255 * cover);
                }
        }
        var offset = (size - art) / 2;
        for (var y = 0; y < art; y++)
            for (var x = 0; x < art; x++)
            {
                var from = (y * art + x) * 4;
                var to = ((y + offset) * size + x + offset) * 4;
                var keep = 1 - artPixels[from + 3] / 255.0;
                for (var channel = 0; channel < 4; channel++)
                    pixels[to + channel] = (byte)Math.Min(255, Math.Round(artPixels[from + channel] + pixels[to + channel] * keep));
            }
        for (var at = 0; at < pixels.Length; at += 4)
        {
            var alpha = pixels[at + 3];
            if (alpha is 0 or 255) continue;
            for (var channel = 0; channel < 3; channel++) pixels[at + channel] = (byte)Math.Min(255, Math.Round(pixels[at + channel] * 255.0 / alpha));
        }
        return pixels;
    }

    /// <summary>How much of the pixel a rounded square covers, from four by four samples.</summary>
    private static double Coverage(int x, int y, int size, double radius)
    {
        var inside = 0;
        for (var sy = 0; sy < 4; sy++)
            for (var sx = 0; sx < 4; sx++)
            {
                double px = x + (sx + 0.5) / 4, py = y + (sy + 0.5) / 4;
                var dx = Math.Max(0, Math.Max(radius - px, px - (size - radius)));
                var dy = Math.Max(0, Math.Max(radius - py, py - (size - radius)));
                if (dx * dx + dy * dy <= radius * radius) inside++;
            }
        return inside / 16.0;
    }

    private bool Remove(string id)
    {
        _framesOnDisk.Clear();
        var removed = false;
        foreach (var path in Frames.Select(frame => Frame(id, frame)).Append(Stamp(id)))
        {
            if (!File.Exists(path)) continue;
            try { File.Delete(path); removed = true; }
            catch (IOException) { }
        }
        return removed;
    }

    private string Frame(string id, int frame) => Path.Combine(directory, id + "-" + frame + ".png");
    private string Stamp(string id) => Path.Combine(directory, id + ".source");
}
