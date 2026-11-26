using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace Sic.App;

internal sealed partial class MainWindow
{
    private async Task<object> CaptureShellAsync(string path, TerminalView terminal)
    {
        var bitmap = await CaptureAsync(_root, path);
        var point = terminal.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point(0, 0));
        return new { path, pixelWidth = bitmap.PixelWidth, pixelHeight = bitmap.PixelHeight,
            rootWidth = _root.ActualWidth, rootHeight = _root.ActualHeight,
            terminalLeft = point.X, terminalTop = point.Y, terminalWidth = terminal.ActualWidth, terminalHeight = terminal.ActualHeight,
            note = "Native XAML content capture. WebView2 is captured separately and must be composited into the measured terminal rectangle." };
    }

    /// <summary>Renders one element, a dialog in its popup included, to a PNG, for review evidence.</summary>
    private static async Task<RenderTargetBitmap> CaptureAsync(Microsoft.UI.Xaml.UIElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        await using var stream = File.Create(path);
        using var randomAccess = stream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, randomAccess);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        return bitmap;
    }

    /// <summary>
    /// The window as Windows composes it, the live terminals' WebView2 content included, which a RenderTargetBitmap leaves out:
    /// PrintWindow with PW_RENDERFULLCONTENT, read back as 32-bit pixels from the window's top-left corner (its invisible
    /// resize borders included) and saved to a PNG. Null when Windows draws nothing, as for a minimized window.
    /// </summary>
    private async Task<(int Width, int Height, byte[] Bgra, int Left, int Top)?> CaptureWindowAsync(string path)
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!GetWindowRect(window, out var bounds)) return null;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return null;
        // Top-down rows (a negative height), four bytes a pixel, blue first.
        var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var context = CreateCompatibleDC(0);
        var section = CreateDIBSection(context, ref header, 0, out var bits, 0, 0);
        var previous = SelectObject(context, section);
        var pixels = new byte[width * height * 4];
        try
        {
            const uint RenderFullContent = 2;
            if (section == 0 || !PrintWindow(window, context, RenderFullContent)) return null;
            Marshal.Copy(bits, pixels, 0, pixels.Length);
        }
        finally { SelectObject(context, previous); DeleteObject(section); DeleteDC(context); }
        await using var stream = File.Create(path);
        using var randomAccess = stream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, randomAccess);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        return (width, height, pixels, bounds.Left, bounds.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ColorsUsed, ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out WindowBounds rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(nint window, nint context, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint context);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint context, ref BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint context, nint item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint context);
}
