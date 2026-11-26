[CmdletBinding()]
param(
    # The fixture's output rate, in kilobytes a second.
    [int]$RateKilobytes = 100,
    # How long the pane's tab stays behind another tab, then how long the window stays in the tray with the pane's tab selected.
    [double]$HiddenMinutes = 10,
    [double]$TrayMinutes = 2,
    # A burst of this many megabytes, printed as fast as the pipe takes it, this many seconds after the tab is hidden; 0 for none.
    [int]$BurstMegabytes = 0,
    [double]$BurstAfterSeconds = 90,
    # Extra Chromium switches for the terminals' browser (WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS), to try a design such as
    # --disable-background-timer-throttling without building it.
    [string]$BrowserArguments,
    [string]$ApplicationPath = (Join-Path $PSScriptRoot '..\artifacts\SicTerm\SicTerm.exe'),
    [string]$ScratchRoot = (Join-Path $env:TEMP 'sic-measure')
)
<#
The hidden-page measurement behind the flow-control item (docs/decisions/performance-pass.md): how far a terminal's page falls behind its output while its tab is not
showing, and while the window is in the tray. Runs the published SicTerm.exe with --measure-hidden-output in a data folder and
agent homes of its own (your settings and sessions are not touched), waits for it, and prints its samples. A window opens for
the length of the run; leave it in view during its first half-minute and while it catches up, which are its in-view figures.
See src\Sic.App\MainWindow.Measure.cs for what each column means.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$application = (Resolve-Path -LiteralPath $ApplicationPath).Path
$run = Join-Path $ScratchRoot ('run-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$data = (New-Item -ItemType Directory -Path (Join-Path $run 'data') -Force).FullName
$report = Join-Path $run 'measure.json'
$start = [Diagnostics.ProcessStartInfo]::new($application)
$start.WorkingDirectory = Split-Path $application -Parent
$start.UseShellExecute = $false
$arguments = @('--measure-hidden-output', '--measure-rate', $RateKilobytes, '--measure-minutes', $HiddenMinutes, '--measure-tray-minutes', $TrayMinutes,
    '--measure-burst', $BurstMegabytes, '--measure-burst-after', $BurstAfterSeconds, '--data-dir', $data, '--report', $report)
foreach ($argument in $arguments) { $start.ArgumentList.Add([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $argument)) }
foreach ($variable in @{ HERMES_HOME = 'hermes'; CLAUDE_CONFIG_DIR = 'claude'; CODEX_HOME = 'codex'; XDG_DATA_HOME = 'share' }.GetEnumerator()) {
    $start.Environment[$variable.Key] = (New-Item -ItemType Directory -Path (Join-Path $run $variable.Value) -Force).FullName
}
if ($BrowserArguments) { $start.Environment['WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS'] = $BrowserArguments }

Write-Host ("Measuring {0} KB/s: {1} min behind another tab{2}, {3} min in the tray{4}. Report: {5}" -f $RateKilobytes, $HiddenMinutes,
    $(if ($BurstMegabytes -gt 0) { " with a $BurstMegabytes MB burst $BurstAfterSeconds s in" } else { '' }), $TrayMinutes,
    $(if ($BrowserArguments) { ", browser arguments '$BrowserArguments'" } else { '' }), $report)
$process = [Diagnostics.Process]::Start($start)
try {
    $budget = [TimeSpan]::FromMinutes($HiddenMinutes + $TrayMinutes + 10)
    if (-not $process.WaitForExit([int]$budget.TotalMilliseconds)) { $process.Kill($true); throw "The measurement did not finish within $budget." }
    if (-not (Test-Path -LiteralPath $report -PathType Leaf)) { throw "The application exited with code $($process.ExitCode) without a report." }
}
finally { $process.Dispose() }

$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if ($result.PSObject.Properties.Name -notcontains 'completed' -or -not $result.completed) { throw "The measurement did not complete: $($result.error)" }
Write-Host ("WebView2 {0}, console host {1}, {2} lines a second; browser arguments '{3}'" -f $result.browserVersion, $result.consoleHost, $result.linesPerSecond, $result.browserArguments)
$result.samples | Format-Table phase, seconds, visibility, behindSeconds, pendingMB, deliveryBehindSeconds, timerWakeups, timerLongestGapMs, heapMB, status -AutoSize | Out-String -Width 220 | Write-Host
Write-Host ("Caught up {0} ms after the tab was shown again, {1} ms after the window came back from the tray (-1: not measured or not caught up). Burst at {2} s. Pane failed: {3} ({4})" -f
    $result.catchUpAfterTabMs, $result.catchUpAfterTrayMs, $result.burstAtSeconds, $result.paneFailed, $result.paneStatus)
$result
