[CmdletBinding()]
param(
    [switch]$Smoke,
    [string]$ScratchRoot = (Join-Path $env:TEMP 'sic-smoke'),
    # A version other than the project's <Version>, for a trial release; Build-Release.ps1 passes it on.
    [string]$Version,
    # Also output the folder published to, as an object with a PublishedTo property, for Build-Release.ps1 to package.
    [switch]$PassThru,
    # Leave out SicExplorerCommand.dll and SicTerm.Identity.msix (Open in SIC on Windows 11's own menu): a build on a PC without the
    # C++ tools or an Azure CLI signed in to sign. Such a copy puts the entry under Show more options instead.
    [switch]$SkipIdentity
)
<#
Publishes SIC to the one place it runs from, and optionally smoke-tests that build.

  artifacts\SicTerm       the default: the copy to run.
  artifacts\SicTerm-next  the standard alternative, used only while SicTerm.exe is running from artifacts\SicTerm and so
                          locks it. Quit SIC and start artifacts\SicTerm-next\SicTerm.exe to take the new build; the next
                          publish goes back to artifacts\SicTerm and removes SicTerm-next once nothing runs from it.

The target folder is emptied first, so no file from an earlier build is left behind. Nothing else under artifacts\ is a
copy of the app. -Smoke runs scripts\Test-AppSmoke.ps1 against the published SicTerm.exe with its own data folder under
-ScratchRoot (keep that short: long paths fail the pane-path check); your own settings and sessions are not touched.
Unless -SkipIdentity, the publish also builds SicExplorerCommand.dll (Build-ExplorerCommand.ps1, needs the Visual Studio Build
Tools' C++ workload) and the signed SicTerm.Identity.msix (Build-IdentityPackage.ps1, signed with Azure Artifact Signing, so it
needs the Azure CLI signed in as an account that may sign: docs/decisions/code-signing.md), which put Open in SIC on Windows
11's own right-click menu.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$default = Join-Path $repo 'artifacts\SicTerm'
$next = Join-Path $repo 'artifacts\SicTerm-next'

function RunningFrom([string]$folder) {
    @(Get-Process SicTerm -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and [IO.Path]::GetDirectoryName($_.Path).TrimEnd('\') -ieq $folder.TrimEnd('\')
    }).Count -gt 0
}

$target = $default
if (RunningFrom $default) {
    if (RunningFrom $next) { throw "SIC is running from both $default and $next. Quit one of them and publish again." }
    $target = $next
}
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }

# The terminal page (the xterm.js bundle under src\Sic.App\Assets\terminal) is built from src\terminal and is not kept in the
# repository: it is built here when it is missing or older than any file in src\terminal, so a fresh clone publishes without a
# separate npm step. `npm run build --prefix .\src\terminal` by hand does the same.
$terminalSource = Join-Path $repo 'src\terminal'
$terminalBundle = Join-Path $repo 'src\Sic.App\Assets\terminal\terminal.js'
$terminalSources = @(Get-ChildItem -LiteralPath $terminalSource -File) + @(Get-ChildItem -LiteralPath (Join-Path $terminalSource 'fonts') -File)
$bundleStale = -not (Test-Path -LiteralPath $terminalBundle) -or
    @($terminalSources | Where-Object { $_.LastWriteTimeUtc -gt (Get-Item -LiteralPath $terminalBundle).LastWriteTimeUtc }).Count -gt 0
if ($bundleStale) {
    if (-not (Test-Path -LiteralPath (Join-Path $terminalSource 'node_modules'))) {
        npm ci --prefix $terminalSource
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed (exit code $LASTEXITCODE)." }
    }
    npm run build --prefix $terminalSource
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed (exit code $LASTEXITCODE)." }
}

# Always an array: an if that yields one string would make it a string, and splatting a string passes it one character at a time.
$versionArguments = @(if ($Version) { "-p:Version=$Version" })
dotnet publish (Join-Path $repo 'src\Sic.App\Sic.App.csproj') -c Release -r win-x64 --self-contained true -o $target @versionArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

if (-not $SkipIdentity) {
    # Open in SIC for Windows 11's own menu: the command DLL, built with the C++ tools, and the signed identity package beside the exe
    # (docs/decisions/explorer-menu.md). -SkipIdentity leaves both out on a PC without the tools or the sign-in.
    $null = & (Join-Path $PSScriptRoot 'Build-ExplorerCommand.ps1') -Destination $target
    $appVersion = if ($Version) { $Version } else { @(([xml](Get-Content -LiteralPath (Join-Path $repo 'src\Sic.App\Sic.App.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ })[0] }
    $null = & (Join-Path $PSScriptRoot 'Build-IdentityPackage.ps1') -PublishedTo $target -Version $appVersion
}

if ($target -eq $default -and (Test-Path -LiteralPath $next)) {
    if (RunningFrom $next) { Write-Warning "SIC is still running from $next. Quit it and start $default\SicTerm.exe; the next publish removes $next." }
    else { Remove-Item -LiteralPath $next -Recurse -Force }
}

if ($Smoke) {
    # The smoke script throws on any failed check.
    & (Join-Path $PSScriptRoot 'Test-AppSmoke.ps1') -ApplicationPath (Join-Path $target 'SicTerm.exe') -ScratchRoot $ScratchRoot -SkipIdentity:$SkipIdentity
}

if ($target -eq $next) { "Published to $target, because SIC is running from $default. Quit SIC and start $target\SicTerm.exe to use this build." }
else { "Published to $target." }
if ($PassThru) { [pscustomobject]@{ PublishedTo = $target } }
