[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ApplicationPath,
    [Parameter(Mandatory)] [string]$DataDir,
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$Theme = 'fluent-dark',
    [string]$Panel = 'sessions',
    [switch]$PanelClosed,
    [switch]$StartShell,
    [switch]$SelectFirstRow,
    [string]$Search,
    [int]$WaitSeconds = 12
)
<#
Launches the app against a scratch data directory, optionally selects the first conversation and starts a
PowerShell tab, then saves a screenshot of the real window: <OutDir>\<Theme>-<Panel>[-closed].png.
Point -DataDir at a COPY of %LOCALAPPDATA%\SuperIntelligentChat (settings.json, layout.json, catalog.db) so the
live data is never touched. Sets PreferredAgent to PowerShell in that copy so -StartShell never launches an agent.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
if (-not ('Win32Input' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Input {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
}
[Win32Input]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
New-Item -ItemType Directory -Force $OutDir | Out-Null
$settingsPath = Join-Path $DataDir 'settings.json'
$settings = if (Test-Path $settingsPath) { Get-Content $settingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{ Version = 1 } }
$settings | Add-Member -NotePropertyName Theme -NotePropertyValue $Theme -Force
$settings | Add-Member -NotePropertyName PreferredAgent -NotePropertyValue 'shell' -Force
$settings | Add-Member -NotePropertyName PanelOpen -NotePropertyValue (-not $PanelClosed) -Force
$settings | Add-Member -NotePropertyName ActivePanel -NotePropertyValue $Panel -Force
$settings | ConvertTo-Json -Depth 8 | Set-Content $settingsPath -Encoding UTF8
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = (Resolve-Path -LiteralPath $ApplicationPath).Path
$start.WorkingDirectory = Split-Path $start.FileName -Parent
$start.UseShellExecute = $false
foreach ($argument in @('--data-dir', $DataDir)) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
try {
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) { $process.Refresh(); if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 300 }
    Start-Sleep -Seconds $WaitSeconds
    $handle = $process.MainWindowHandle
    [Win32Input]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 500
    $rect = New-Object Win32Input+RECT
    [Win32Input]::DwmGetWindowAttribute($handle, 9, [ref]$rect, 16) | Out-Null
    if ($Search) {
        [System.Windows.Forms.SendKeys]::SendWait('^k')
        Start-Sleep -Milliseconds 600
        [System.Windows.Forms.SendKeys]::SendWait($Search)
        Start-Sleep -Milliseconds 1500
    }
    if ($SelectFirstRow) {
        # First conversation row: title bar 40 + panel header 44 + search 42 + selector 46 + count 26, then half a row; the rail is 48 wide.
        $x = $rect.Left + 48 + 160; $y = $rect.Top + 40 + 44 + 42 + 46 + 26 + 26
        [Win32Input]::SetCursorPos($x, $y) | Out-Null
        [Win32Input]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [Win32Input]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 1200
    }
    if ($StartShell) {
        [Win32Input]::SetForegroundWindow($handle) | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait('^+t')
        Start-Sleep -Milliseconds 1500
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Start-Sleep -Seconds 4
        [System.Windows.Forms.SendKeys]::SendWait('Get-ChildItem | Select-Object -First 8{ENTER}')
        Start-Sleep -Seconds 3
    }
    & (Join-Path $PSScriptRoot 'Capture-Window.ps1') -ProcessId $process.Id -OutFile (Join-Path $OutDir "$Theme-$Panel$(if ($PanelClosed) { '-closed' })$(if ($Search) { '-search' }).png")
}
finally {
    if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
