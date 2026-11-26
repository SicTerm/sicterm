using System.Runtime.InteropServices;
using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class WindowsCommandLineTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("two words")]
    [InlineData("")]
    [InlineData("C:\\space path\\trailing\\")]
    [InlineData("a\"b")]
    [InlineData("a\\\"b")]
    [InlineData("literal;&|$()%!")]
    [InlineData("日本語 résumé")]
    [InlineData("\t")]
    public void QuoteArgument_RoundTripsThroughTheWindowsArgumentParser(string argument)
    {
        var commandLine = "probe.exe " + WindowsCommandLine.QuoteArgument(argument);
        var pointer = CommandLineToArgvW(commandLine, out var count);
        Assert.NotEqual(IntPtr.Zero, pointer);
        try
        {
            Assert.Equal(2, count);
            Assert.Equal(argument, Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, IntPtr.Size)));
        }
        finally
        {
            _ = LocalFree(pointer);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad\0argument")]
    public void QuoteArgument_RejectsNullOrNul(string? argument)
    {
        Assert.ThrowsAny<ArgumentException>(() => WindowsCommandLine.QuoteArgument(argument!));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
