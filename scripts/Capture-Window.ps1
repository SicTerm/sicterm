[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int]$ProcessId,
    [Parameter(Mandatory)] [string]$OutFile,
    [int]$SettleMs = 1200
)
# Captures one top-level window to a PNG through PrintWindow (renders the window itself, whatever is in front of it),
# falling back to a screen copy. DPI aware; the invisible resize borders are cropped away. Used by Review-Shell.ps1.
Add-Type -AssemblyName System.Drawing
if (-not ('Win32Capture' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Capture {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
}
[Win32Capture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
$process = Get-Process -Id $ProcessId
$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw "Process $ProcessId has no main window yet." }
[Win32Capture]::ShowWindow($handle, 9) | Out-Null
[Win32Capture]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds $SettleMs
$window = New-Object Win32Capture+RECT
[Win32Capture]::GetWindowRect($handle, [ref]$window) | Out-Null
$frame = New-Object Win32Capture+RECT
[Win32Capture]::DwmGetWindowAttribute($handle, 9, [ref]$frame, 16) | Out-Null
$fullWidth = $window.Right - $window.Left; $fullHeight = $window.Bottom - $window.Top
if ($fullWidth -le 0 -or $fullHeight -le 0) { throw 'Window rectangle is empty.' }
$full = New-Object System.Drawing.Bitmap $fullWidth, $fullHeight
$graphics = [System.Drawing.Graphics]::FromImage($full)
$hdc = $graphics.GetHdc()
$printed = [Win32Capture]::PrintWindow($handle, $hdc, 2)
$graphics.ReleaseHdc($hdc)
if (-not $printed) { $graphics.CopyFromScreen($window.Left, $window.Top, 0, 0, $full.Size) }
$graphics.Dispose()
$cropX = [Math]::Max(0, $frame.Left - $window.Left); $cropY = [Math]::Max(0, $frame.Top - $window.Top)
$cropWidth = [Math]::Min($frame.Right - $frame.Left, $fullWidth - $cropX); $cropHeight = [Math]::Min($frame.Bottom - $frame.Top, $fullHeight - $cropY)
$cropped = $full.Clone((New-Object System.Drawing.Rectangle $cropX, $cropY, $cropWidth, $cropHeight), $full.PixelFormat)
$cropped.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$cropped.Dispose(); $full.Dispose()
"saved $OutFile ${cropWidth}x${cropHeight} (printwindow=$printed)"