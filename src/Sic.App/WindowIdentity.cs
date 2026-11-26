using System.Runtime.InteropServices;

namespace Sic.App;

/// <summary>
/// The taskbar identity of SIC's window. An installed SIC runs under the AppUserModelID of its Start menu shortcut
/// (<c>velopack.SicTerm</c>, which Velopack sets on the process and the shortcut), and Windows then draws the taskbar button
/// with that shortcut's icon, the one built into SicTerm.exe, whatever icon the window hands it: seen in Windows Sandbox on
/// October 1, 2026, with Settings → App icon on Coral tile and the shipped mark on the button. A Settings → App icon option
/// other than the shipped one therefore gives the window an ID of its own, which no shortcut carries, so the taskbar draws the
/// window's icon; such a window is kept from being pinned, so a trial icon leaves no pin behind. The shipped option clears the
/// window's ID, and the window rejoins its shortcut. The property store is driven through its function table rather than a
/// COM interface type, because the app's ComWrappers handle COM marshalling and know only WinRT types.
/// </summary>
internal static class WindowIdentity
{
    private static readonly Guid AppUserModelKeys = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint IdKey = 5, PreventPinningKey = 9;
    private const ushort Empty = 0, Boolean = 11, WideString = 31;

    /// <summary>Gives the window its own AppUserModelID, or with null clears it so the window takes the process's again.</summary>
    internal static void Set(nint window, string? id)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
        try
        {
            var setValue = Marshal.GetDelegateForFunctionPointer<SetValueMethod>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(store), 6 * IntPtr.Size));
            if (id is null)
            {
                // The ID first: PreventPinning applies only while a window has an ID of its own.
                Write(setValue, store, IdKey, new PropertyValue { Type = Empty });
                Write(setValue, store, PreventPinningKey, new PropertyValue { Type = Empty });
                return;
            }
            // PreventPinning must be set before the ID (System.AppUserModel.PreventPinning).
            Write(setValue, store, PreventPinningKey, new PropertyValue { Type = Boolean, Flag = -1 });
            var text = Marshal.StringToCoTaskMemUni(id);
            try { Write(setValue, store, IdKey, new PropertyValue { Type = WideString, Pointer = text }); }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        finally { Marshal.Release(store); }
    }

    /// <summary>The window's own AppUserModelID as Windows holds it, or null when it has none.</summary>
    internal static string? Get(nint window)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
        try
        {
            var getValue = Marshal.GetDelegateForFunctionPointer<GetValueMethod>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(store), 5 * IntPtr.Size));
            var key = new PropertyKey { FormatId = AppUserModelKeys, PropertyId = IdKey };
            Marshal.ThrowExceptionForHR(getValue(store, ref key, out var value));
            try { return value.Type == WideString ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.Release(store); }
    }

    private static void Write(SetValueMethod setValue, nint store, uint property, PropertyValue value)
    {
        var key = new PropertyKey { FormatId = AppUserModelKeys, PropertyId = property };
        Marshal.ThrowExceptionForHR(setValue(store, ref key, ref value));
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetValueMethod(nint self, ref PropertyKey key, ref PropertyValue value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetValueMethod(nint self, ref PropertyKey key, out PropertyValue value);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropertyValue value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid FormatId; public uint PropertyId; }
    /// <summary>A PROPVARIANT: the type, then the value at offset 8; 24 bytes on 64-bit Windows.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyValue
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
        [FieldOffset(8)] public short Flag;
    }
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid, out nint store);
}
