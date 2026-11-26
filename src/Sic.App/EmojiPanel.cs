using System.Runtime.InteropServices;

namespace Sic.App;

/// <summary>
/// Opens Windows' own emoji panel over the focused text box by sending the Win + . chord, which is what the panel answers to;
/// no API opens it for a desktop window. The panel is an input window of Windows' own, like an IME's, so it takes no
/// activation from SIC and the flyout holding the box stays open while it is used. Sent input is refused while the session
/// is locked, which <see cref="Open"/> reports as false.
/// </summary>
internal static class EmojiPanel
{
    private const ushort LeftWindows = 0x5B, Period = 0xBE;
    private const uint KeyboardInput = 1, KeyUp = 0x0002;

    /// <summary>Sends Win + . as four key events; true when Windows took them all.</summary>
    internal static bool Open()
    {
        var inputs = new[] { Key(LeftWindows, up: false), Key(Period, up: false), Key(Period, up: true), Key(LeftWindows, up: true) };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static INPUT Key(ushort key, bool up) => new() { Type = KeyboardInput, Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = up ? KeyUp : 0 } } };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint Type; public InputUnion Union; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort VirtualKey; public ushort Scan; public uint Flags; public uint Time; public nint ExtraInfo; }

    // Present so the union is the size Windows expects: the mouse member is the largest.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public nint ExtraInfo; }

    [DllImport("user32.dll", EntryPoint = "SendInput", ExactSpelling = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
