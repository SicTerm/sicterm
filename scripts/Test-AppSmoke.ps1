[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ApplicationPath,
    [Parameter(Mandatory)] [string]$ScratchRoot,
    # The build left out SicExplorerCommand.dll and SicTerm.Identity.msix on purpose (Publish-App.ps1 -SkipIdentity): the app is told,
    # skips the Windows 11 menu's checks, and reports identitySkipped, which this script then requires in place of them.
    [switch]$SkipIdentity
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $ApplicationPath -PathType Leaf)) {
    Write-Host "FAIL: The runnable application does not exist: $ApplicationPath"
    exit 1
}

$runDirectory = Join-Path $ScratchRoot ('sic-ui-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $runDirectory -Force
$reportPath = Join-Path $runDirectory 'terminal-smoke.json'
Write-Host "Smoke report: $reportPath"
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = (Resolve-Path -LiteralPath $ApplicationPath).Path
$start.WorkingDirectory = Split-Path $start.FileName -Parent
$start.UseShellExecute = $false
foreach ($argument in @('--terminal-smoke-test', '--data-dir', $runDirectory, '--report', $reportPath) + @(if ($SkipIdentity) { '--without-identity' })) {
    $start.ArgumentList.Add($argument)
}

$process = [Diagnostics.Process]::Start($start)
try {
    # About 75 s since the ready chime's check, which waits out real turns past PresenceTracker.MinimumTurn (September 30, 2026).
    if (-not $process.WaitForExit(150000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'The isolated application smoke test exceeded 150 seconds.'
    }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "The app exited with code $($process.ExitCode) without producing its test report."
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $required = @(
        'success', 'terminalReady', 'inputEchoed', 'resized', 'workspaceShell', 'brandMark', 'historySearch', 'previewShown', 'renamedSearch',
        'placeholderRestored', 'reopenedNamed', 'inlineRename', 'f2Rename', 'f2FromTerminal', 'tabColored', 'reopenedColored', 'tabStripLegible', 'activeTabJoinsPane', 'harnessNameDropped', 'panePathFits', 'closeToTray', 'minimizeToTray', 'closeAndMinimize', 'paneBorderPreview', 'findMatched', 'dropPastesQuotedPath', 'splitCreated', 'splitRefused', 'splitTiled', 'splitSaved', 'paneFocusMoved', 'paneRenamed', 'tabDuplicated', 'resumeJoined', 'failedStartRecovered', 'backgroundStartWaited', 'resumeAll', 'resumeAllButton', 'noticeToasted', 'f5ResumesAll', 'keyboardHelp', 'resumeAllOnLaunch', 'hiddenWindowStart', 'dragRegions', 'sidebarRight', 'presenceHeuristic', 'presenceTitle', 'readyChime', 'activeTabColored', 'panelCollapsed', 'panelExpanded', 'panelSlid', 'panelResized',
        'workspacesPanel', 'workspacesGrouped', 'workspaceFolders', 'suggestedWorkspaces', 'workspaceFromFolder', 'sessionsGrouped', 'sectionsRuled', 'pinMarkDrawn', 'newSessionFoldersListed', 'folderEmoji', 'sounds', 'folderExplorer', 'folderRowsPresented', 'updatesSection', 'appIconShipped', 'themeKeptScroll', 'userThemes', 'motionSetting', 'terminalOptionsApplied', 'cursorDrawn', 'copyOnSelect', 'terminalPaletteFixed', 'themesApplied', 'railAccentReadable', 'lightThemeApplied', 'darkThemeApplied', 'lightStartupApplied', 'gracefulExit', 'alwaysExitCleanly', 'quitDialog', 'windowPlacementTracked',
        'consoleHost', 'zeroWidth', 'cursorRowsAgree', 'queryAnswersHidden',
        'petImageDrawn', 'petImageCleared', 'petImageDeleted', 'petImagesOff', 'petImagesOffAtStart',
        'openFolderReceived', 'openAgentStarted', 'openMissingRefused', 'explorerCommandBuilt', 'explorerPackageSigned', 'explorerMenuOwned', 'explorerCommandEnumerated', 'panelKeptByResumeAll',
        'contextWindow'
    )
    $identityChecks = @('explorerCommandBuilt', 'explorerPackageSigned', 'explorerMenuOwned', 'explorerCommandEnumerated')
    if ($SkipIdentity) { $required = @($required | Where-Object { $_ -notin $identityChecks }) + 'identitySkipped' }
    foreach ($field in $required) {
        if ($report.PSObject.Properties.Name -notcontains $field -or $report.$field -ne $true) {
            throw "Application check '$field' failed. Report: $reportPath"
        }
    }
    if ($process.ExitCode -ne 0) {
        throw "Application returned exit code $($process.ExitCode). Report: $reportPath"
    }
    Write-Host 'PASS: Native WinUI shell, the sidebar on either side, grouped and pinned conversations with ruled group labels and the drawn pin, workspaces following the Folders list (a folder added takes its tabs, Scratch comes and goes with a stray tab, a folder removed gives its tabs back) and a second one restored, the Folders list adopted from the old folder list with its tree, emoji, color and sound, Open in File Explorer on folder rows and the listed folder, folder rows that come back on every expand, folders from the history and from a folder row, Settings → Updates in a copy that is not installed, every Settings → App icon option on the taskbar, title bar and tray, a theme chosen in Settings without moving the panel, the Menu Animation setting, the ready chime, terminal options, the cursor as drawn and copy on select, a terminal palette no theme changes, in-place preview, in-place rename, tab colors, split trees of six panes with a refused split, Alt+Arrow between the panes and onto the tab strip, Alt with a tab number, and pane names, duplicate tab, a second resume of one pane waiting for the first, a failed start giving back its placeholder, starts in a background tab and in the tray, Resume all tab by tab with its rail button, F5 and launch setting, the F1 keyboard reference, the five Settings tabs, an agent card and its switch, an agent switched off and on again across Sessions, Resume all and New session, window drag areas by hit test, agent state on the tab, find, a dropped path, panel width, every theme, tray, Close and minimize, Always exit cleanly and the quit dialog, window placement tracking, a real console round-trip through the packaged console host (a combining accent one column wide, cursor rows that agree after a resize, no query answers on screen), and a kitty pet image through it drawn in its box, hidden by a clear, removed by a delete, and left alone with Pet images off.'
    Write-Host "Report: $reportPath"
    $report | ConvertTo-Json -Depth 8

    $restoreReport = Join-Path $runDirectory 'restore-smoke.json'
    $restoreStart = [Diagnostics.ProcessStartInfo]::new()
    $restoreStart.FileName = $start.FileName
    $restoreStart.WorkingDirectory = $start.WorkingDirectory
    $restoreStart.UseShellExecute = $false
    foreach ($argument in @('--restore-smoke-test', '--data-dir', $runDirectory, '--report', $restoreReport)) { $restoreStart.ArgumentList.Add($argument) }
    $restored = [Diagnostics.Process]::Start($restoreStart)
    try {
        if (-not $restored.WaitForExit(30000)) { $restored.Kill($true); throw 'Restored application did not exit within 30 seconds.' }
        if (-not (Test-Path -LiteralPath $restoreReport -PathType Leaf)) { throw 'The second application process did not produce a restoration report.' }
        $restoredReport = Get-Content -LiteralPath $restoreReport -Raw | ConvertFrom-Json
        foreach ($field in @('success', 'restoredWithoutProcesses', 'workspacesRestored', 'themeRestored', 'nameRestored', 'collapsedRestored', 'panelWidthRestored', 'colorRestored', 'treeRestored', 'paneNameRestored', 'windowPlacementRestored', 'pinRestored', 'foldersRestored', 'soundRestored', 'terminalOptionsRestored', 'alwaysExitCleanlyRestored', 'resumeAllOnLaunchRestored', 'sidebarRestored', 'readyChimeRestored', 'petImagesRestored', 'appIconRestored')) {
            if ($restoredReport.PSObject.Properties.Name -notcontains $field -or $restoredReport.$field -ne $true) {
                throw "Second-process restoration check '$field' failed: $restoreReport"
            }
        }
        if ($restored.ExitCode -ne 0) { throw "Second-process restoration failed: $restoreReport" }
        Write-Host 'PASS: A new application process restored saved tabs, a six-pane split tree with its pane names, theme, names, tab colors, the window placement, the panel width, the sidebar on the right, the pinned conversation, the Folders list, the terminal options, Always exit cleanly, Resume sessions on launch, the ready chime, Pet images off, the app icon and the collapsed panel without starting terminals.'
        $restoredReport | ConvertTo-Json -Depth 8
    }
    finally { $restored.Dispose() }

    # SicTerm.exe --quit: an ordinary launch (no test mode) with a data folder and empty agent homes of its own, so it reads no
    # real history, quits by itself when a second process asks with --quit for the same folder, as the tray menu's Quit does.
    $quitRoot = Join-Path $ScratchRoot ('sic-quit-' + [Guid]::NewGuid().ToString('N'))
    $quitData = Join-Path $quitRoot 'data'
    # It opens with Settings as the restored panel, on General, whose app-icon previews are images: that
    # ended SIC in Microsoft.UI.Xaml.dll a second or two after launch until Settings' content waited for the window to load (October 5, 2026).
    New-Item -ItemType Directory -Path $quitData -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $quitData 'settings.json') -Value '{"Version":1,"PanelOpen":true,"ActivePanel":"settings","PanelWidth":560,"SettingsTab":"general"}' -Encoding utf8NoBOM
    $quitStart = [Diagnostics.ProcessStartInfo]::new()
    $quitStart.FileName = $start.FileName
    $quitStart.WorkingDirectory = $start.WorkingDirectory
    $quitStart.UseShellExecute = $false
    foreach ($argument in @('--data-dir', $quitData)) { $quitStart.ArgumentList.Add($argument) }
    foreach ($variable in @{ HERMES_HOME = 'hermes'; CLAUDE_CONFIG_DIR = 'claude'; CODEX_HOME = 'codex'; XDG_DATA_HOME = 'share' }.GetEnumerator()) {
        $quitStart.Environment[$variable.Key] = (New-Item -ItemType Directory -Path (Join-Path $quitRoot $variable.Value) -Force).FullName
    }
    $running = [Diagnostics.Process]::Start($quitStart)
    try {
        # The window is tagged as its data folder's instance as it is built; a --quit before that finds nothing to ask.
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while ([DateTime]::UtcNow -lt $deadline -and -not $running.HasExited -and $running.MainWindowHandle -eq 0) { Start-Sleep -Milliseconds 250; $running.Refresh() }
        if ($running.HasExited) { throw "The application started for the --quit check exited by itself with code $($running.ExitCode)." }
        if ($running.MainWindowHandle -eq 0) { throw 'The application started for the --quit check showed no window within 45 seconds.' }
        Start-Sleep -Seconds 5
        if ($running.HasExited) { throw "The application started with Settings open at launch exited by itself with code $($running.ExitCode)." }
        $askStart = [Diagnostics.ProcessStartInfo]::new($start.FileName)
        $askStart.UseShellExecute = $false
        foreach ($argument in @('--quit', '--data-dir', $quitData)) { $askStart.ArgumentList.Add($argument) }
        $asker = [Diagnostics.Process]::Start($askStart)
        if (-not $asker.WaitForExit(15000)) { $asker.Kill($true); throw 'SicTerm.exe --quit did not return within 15 seconds.' }
        if ($asker.ExitCode -ne 0) { throw "SicTerm.exe --quit returned exit code $($asker.ExitCode)." }
        if (-not $running.WaitForExit(30000)) { throw 'The running application did not quit within 30 seconds of SicTerm.exe --quit.' }
        if ($running.ExitCode -ne 0) { throw "The application asked to quit by SicTerm.exe --quit returned exit code $($running.ExitCode)." }
        Write-Host 'PASS: SIC opened with Settings restored on General, and SicTerm.exe --quit made it quit as the tray menu Quit does.'
    }
    finally {
        if (-not $running.HasExited) { $running.Kill($true) }
        $running.Dispose()
    }
}
finally {
    $process.Dispose()
}
