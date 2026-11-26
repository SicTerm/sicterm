using System.Runtime.InteropServices;
using Sic.Core.Shell;

namespace Sic.App;

/// <summary>
/// For the smoke test: loads SicExplorerCommand.dll into this process the way File Explorer would, through its class factory,
/// points it at a scratch menu key with SIC_EXPLORER_MENU_KEY, and reads back the title, flags, state and subcommands it would
/// show. Nothing here registers anything; the DLL's COM registration is the identity package's business.
/// </summary>
internal static class ExplorerCommandProbe
{
    internal const string KeyVariable = "SIC_EXPLORER_MENU_KEY";
    internal static readonly Guid CommandClass = new("4D6B3C9A-2F1E-4A8B-B7C5-0E9D1A2B3C4D");
    /// <summary>EXPCMDFLAGS and EXPCMDSTATE values: ECF_HASSUBCOMMANDS, ECF_SEPARATORAFTER; ECS_HIDDEN, ECS_ENABLED.</summary>
    internal const uint HasSubCommands = 0x1, SeparatorAfter = 0x40, Hidden = 0x2, Enabled = 0x0;

    internal sealed record Command(string Title, string? Icon, uint Flags, uint State, IReadOnlyList<Command> SubCommands);

    /// <summary>Creates the command against <paramref name="menuKey"/> and reads it, with the DLL unloaded again afterwards.</summary>
    internal static Command Read(string dllPath, string menuKey)
    {
        var previous = Environment.GetEnvironmentVariable(KeyVariable);
        Environment.SetEnvironmentVariable(KeyVariable, menuKey);
        var library = NativeLibrary.Load(dllPath);
        try
        {
            var getClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObject>(NativeLibrary.GetExport(library, "DllGetClassObject"));
            var classId = CommandClass;
            var factoryId = typeof(IClassFactory).GUID;
            Marshal.ThrowExceptionForHR(getClassObject(ref classId, ref factoryId, out var factoryPointer));
            var factory = (IClassFactory)Marshal.GetObjectForIUnknown(factoryPointer);
            Marshal.Release(factoryPointer);
            var commandId = typeof(IExplorerCommand).GUID;
            Marshal.ThrowExceptionForHR(factory.CreateInstance(0, ref commandId, out var commandPointer));
            var command = (IExplorerCommand)Marshal.GetObjectForIUnknown(commandPointer);
            Marshal.Release(commandPointer);
            var result = Describe(command, subCommands: true);
            Marshal.ReleaseComObject(command);
            Marshal.ReleaseComObject(factory);
            return result;
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyVariable, previous);
            NativeLibrary.Free(library);
        }
    }

    private static Command Describe(IExplorerCommand command, bool subCommands)
    {
        Marshal.ThrowExceptionForHR(command.GetTitle(0, out var title));
        var icon = command.GetIcon(0, out var iconText) == 0 ? iconText : null;
        Marshal.ThrowExceptionForHR(command.GetFlags(out var flags));
        Marshal.ThrowExceptionForHR(command.GetState(0, true, out var state));
        var items = new List<Command>();
        if (subCommands && command.EnumSubCommands(out var enumerator) == 0)
        {
            var buffer = new IExplorerCommand[1];
            while (enumerator.Next(1, buffer, out var fetched) == 0 && fetched == 1)
            {
                items.Add(Describe(buffer[0], subCommands: false));
                Marshal.ReleaseComObject(buffer[0]);
            }
            Marshal.ReleaseComObject(enumerator);
        }
        return new Command(title, icon, flags, state, items);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetClassObject(ref Guid classId, ref Guid interfaceId, out nint result);

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig] int CreateInstance(nint outer, ref Guid interfaceId, out nint result);
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
    }

    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(nint items, [MarshalAs(UnmanagedType.LPWStr)] out string title);
        [PreserveSig] int GetIcon(nint items, [MarshalAs(UnmanagedType.LPWStr)] out string icon);
        [PreserveSig] int GetToolTip(nint items, out nint tooltip);
        [PreserveSig] int GetCanonicalName(out Guid name);
        [PreserveSig] int GetState(nint items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);
        [PreserveSig] int Invoke(nint items, nint bindContext);
        [PreserveSig] int GetFlags(out uint flags);
        [PreserveSig] int EnumSubCommands(out IEnumExplorerCommand commands);
    }

    [ComImport, Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumExplorerCommand
    {
        [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IExplorerCommand[] commands, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumExplorerCommand clone);
    }

    /// <summary>Whether the package beside the exe is signed, and by the subject the manifests name; null when it is not there.</summary>
    internal static bool? PackageSignedBy(string directory, string subject)
    {
        var path = Path.Combine(directory, ExplorerMenu.PackageFileName);
        if (!File.Exists(path)) return null;
        try
        {
            // Reading the signer of an Authenticode-signed file has no X509CertificateLoader equivalent; the old call still works.
#pragma warning disable SYSLIB0057
            using var certificate = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return string.Equals(certificate.Subject, subject, StringComparison.Ordinal);
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }
}
