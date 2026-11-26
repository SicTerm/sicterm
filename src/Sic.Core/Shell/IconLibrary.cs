using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Sic.Core.Shell;

/// <summary>One square frame of an icon: <see cref="Size"/> px a side, its pixels as BGRA with straight alpha, top row first.</summary>
public sealed record IconImage(int Size, byte[] Bgra);

/// <summary>An icon as a resource: its group id, as <c>path,-id</c> names it, and its frames.</summary>
public sealed record IconGroup(int Id, IReadOnlyList<IconImage> Images);

/// <summary>
/// A resource-only DLL holding icons (docs/decisions/agent-icons.md). Windows 11's own File Explorer menu shows an icon only
/// from a PE resource (<c>path,-id</c>), not from an .ico or a .png, so an agent whose own icon is not inside an exe (Hermes's
/// icon.ico, the Codex desktop app's Start-tile logo) has it written into one of these. The file is a 32-bit image with one
/// section, .rsrc, and no code: Windows reads it as data (LoadLibraryEx with LOAD_LIBRARY_AS_DATAFILE), never runs it. Each frame
/// is a 32-bit DIB with an empty mask, as an .ico stores one.
/// </summary>
public static class IconLibrary
{
    private const int FileAlignment = 0x200, SectionAlignment = 0x1000, SectionRva = 0x1000;
    private const int RtIcon = 3, RtGroupIcon = 14;

    /// <summary>The file name for a library of these bytes: named by its content, so Explorer's icon cache never shows an older one under the same name.</summary>
    public static string FileName(byte[] library) => "menu-icons-" + Convert.ToHexString(SHA256.HashData(library))[..12].ToLowerInvariant() + ".dll";

    /// <summary>The DLL's bytes. Group ids and frame sizes (1 to 256 px) must be distinct within the library and within each group.</summary>
    public static byte[] Build(IReadOnlyList<IconGroup> groups)
    {
        if (groups.Count == 0) throw new ArgumentException("A library needs at least one icon.", nameof(groups));
        if (groups.Select(group => group.Id).Distinct().Count() != groups.Count || groups.Any(group => group.Id is < 1 or > 0xFFFF || group.Images.Count == 0))
            throw new ArgumentException("Each icon needs its own id from 1 to 65535 and at least one frame.", nameof(groups));
        // Frames are RT_ICON resources numbered from 1 across the library; each group lists its own.
        var icons = new List<(int Id, byte[] Data)>();
        var groupData = new List<(int Id, byte[] Data)>();
        foreach (var group in groups.OrderBy(group => group.Id))
        {
            var images = group.Images.OrderBy(image => image.Size).ToList();
            if (images.Select(image => image.Size).Distinct().Count() != images.Count) throw new ArgumentException("A group has two frames of one size.", nameof(groups));
            var directory = new byte[6 + 14 * images.Count];
            BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(4), (ushort)images.Count);
            for (var index = 0; index < images.Count; index++)
            {
                var dib = Dib(images[index]);
                icons.Add((icons.Count + 1, dib));
                var entry = directory.AsSpan(6 + 14 * index);
                entry[0] = entry[1] = (byte)(images[index].Size == 256 ? 0 : images[index].Size);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)dib.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[12..], (ushort)icons.Count);
            }
            groupData.Add((group.Id, directory));
        }
        var resources = ResourceSection([(RtIcon, icons), (RtGroupIcon, groupData)]);
        return Image(resources);
    }

    /// <summary>A frame as a 32-bit DIB: the header with twice the height (image and mask), the rows bottom first, then an empty AND mask.</summary>
    private static byte[] Dib(IconImage image)
    {
        var size = image.Size;
        if (size is < 1 or > 256 || image.Bgra.Length != size * size * 4) throw new ArgumentException("A frame's pixels do not fit its size.", nameof(image));
        var maskRow = (size + 31) / 32 * 4;
        var dib = new byte[40 + size * size * 4 + maskRow * size];
        var header = dib.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(header, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], size);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], size * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], size * size * 4);
        for (var row = 0; row < size; row++)
            image.Bgra.AsSpan(row * size * 4, size * 4).CopyTo(dib.AsSpan(40 + (size - 1 - row) * size * 4));
        return dib;
    }

    /// <summary>
    /// The .rsrc section: the type directory, a name directory per type, a language directory per resource (neutral), the data
    /// entries, then the data, each piece on a 4-byte boundary. Entries go in id order, as Windows searches them.
    /// </summary>
    private static byte[] ResourceSection(IReadOnlyList<(int Type, List<(int Id, byte[] Data)> Items)> types)
    {
        static int Directory(int entries) => 16 + 8 * entries;
        var all = types.SelectMany(type => type.Items).ToList();
        var namesAt = Directory(types.Count);
        var languagesAt = namesAt + types.Sum(type => Directory(type.Items.Count));
        var entriesAt = languagesAt + all.Count * Directory(1);
        var dataAt = entriesAt + all.Count * 16;
        var length = dataAt + all.Sum(item => Align(item.Data.Length, 4));
        var section = new byte[length];
        var span = section.AsSpan();

        void WriteDirectory(int at, int entries) => BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(at + 14), (ushort)entries);
        void WriteEntry(int at, int index, int id, int target, bool subdirectory)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(at + 16 + 8 * index), (uint)id);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(at + 20 + 8 * index), (uint)target | (subdirectory ? 0x80000000u : 0));
        }

        WriteDirectory(0, types.Count);
        int nameDirectory = namesAt, resource = 0, data = dataAt;
        for (var typeIndex = 0; typeIndex < types.Count; typeIndex++)
        {
            var (type, items) = types[typeIndex];
            WriteEntry(0, typeIndex, type, nameDirectory, subdirectory: true);
            WriteDirectory(nameDirectory, items.Count);
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++, resource++)
            {
                var languageDirectory = languagesAt + resource * Directory(1);
                var entry = entriesAt + resource * 16;
                WriteEntry(nameDirectory, itemIndex, items[itemIndex].Id, languageDirectory, subdirectory: true);
                WriteDirectory(languageDirectory, 1);
                WriteEntry(languageDirectory, 0, 0, entry, subdirectory: false);
                BinaryPrimitives.WriteUInt32LittleEndian(span[entry..], (uint)(SectionRva + data));
                BinaryPrimitives.WriteUInt32LittleEndian(span[(entry + 4)..], (uint)items[itemIndex].Data.Length);
                items[itemIndex].Data.CopyTo(span[data..]);
                data += Align(items[itemIndex].Data.Length, 4);
            }
            nameDirectory += Directory(items.Count);
        }
        return section;
    }

    /// <summary>The DLL around the section: MS-DOS stub header, PE headers for a 32-bit DLL without code or entry point, one section.</summary>
    private static byte[] Image(byte[] resources)
    {
        var raw = Align(resources.Length, FileAlignment);
        var file = new byte[FileAlignment + raw];
        var span = file.AsSpan();
        span[0] = (byte)'M'; span[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], 0x40);
        "PE\0\0"u8.CopyTo(span[0x40..]);
        // COFF header: i386, one section, a 224-byte optional header; an executable image, 32-bit, a DLL.
        var coff = span[0x44..];
        BinaryPrimitives.WriteUInt16LittleEndian(coff, 0x14C);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[16..], 0xE0);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[18..], 0x2102);
        // Optional header (PE32).
        var optional = span[0x58..];
        BinaryPrimitives.WriteUInt16LittleEndian(optional, 0x10B);
        optional[2] = 14;
        BinaryPrimitives.WriteInt32LittleEndian(optional[8..], raw);
        BinaryPrimitives.WriteInt32LittleEndian(optional[24..], SectionRva);
        BinaryPrimitives.WriteInt32LittleEndian(optional[28..], 0x10000000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[32..], SectionAlignment);
        BinaryPrimitives.WriteInt32LittleEndian(optional[36..], FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[40..], 6);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[48..], 6);
        BinaryPrimitives.WriteInt32LittleEndian(optional[56..], SectionRva + Align(resources.Length, SectionAlignment));
        BinaryPrimitives.WriteInt32LittleEndian(optional[60..], FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[68..], 2);
        // NX-compatible and no SEH; not relocatable, as it has no relocations to apply.
        BinaryPrimitives.WriteUInt16LittleEndian(optional[70..], 0x0500);
        BinaryPrimitives.WriteInt32LittleEndian(optional[72..], 0x100000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[76..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[80..], 0x100000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[84..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[92..], 16);
        // Data directory 2, the resource table.
        BinaryPrimitives.WriteInt32LittleEndian(optional[(96 + 16)..], SectionRva);
        BinaryPrimitives.WriteInt32LittleEndian(optional[(96 + 20)..], resources.Length);
        // The section header.
        var section = span[(0x58 + 0xE0)..];
        ".rsrc"u8.CopyTo(section);
        BinaryPrimitives.WriteInt32LittleEndian(section[8..], resources.Length);
        BinaryPrimitives.WriteInt32LittleEndian(section[12..], SectionRva);
        BinaryPrimitives.WriteInt32LittleEndian(section[16..], raw);
        BinaryPrimitives.WriteInt32LittleEndian(section[20..], FileAlignment);
        BinaryPrimitives.WriteUInt32LittleEndian(section[36..], 0x40000040);
        resources.CopyTo(span[FileAlignment..]);
        return file;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
