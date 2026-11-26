[CmdletBinding()]
param(
    # The release's version. The project's <Version> when not given; another, such as 0.9.1-trial, only for a local trial.
    [string]$Version,
    # Also create the GitHub release v<Version> in the repository with the packages.
    [switch]$Upload,
    # Leave the app and Setup unsigned, for a local trial on a PC that cannot sign; never with -Upload.
    [switch]$Unsigned,
    [switch]$SkipSmoke,
    # Also build SicTerm-win.msi, which installs SicTerm for every user of a PC into Program Files (an administrator's install).
    [switch]$Msi,
    [string]$ScratchRoot = (Join-Path $env:TEMP 'sic-smoke'),
    # Where the packages go. artifacts\releases unless a trial needs a folder of its own.
    [string]$OutputDir
)
<#
Builds an installable SIC release with Velopack (docs\decisions\installer.md). Publishes the app with scripts\Publish-App.ps1,
smoke-tests it unless -SkipSmoke, and packs that folder into the releases folder, artifacts\releases by default:

  SicTerm-win-Setup.exe          the installer: for the current user, no admin rights, into %LOCALAPPDATA%\SicTerm, with Start
                                 menu and desktop shortcuts and an entry in Settings → Apps to uninstall it
  SicTerm-win-Portable.zip       the same app to unzip and run anywhere
  SicTerm-<version>-full.nupkg   what an installed SIC downloads to update, with a -delta package from the release before when
                                 that one is still in the folder
  releases.win.json              the list of releases an installed SIC reads
  SicTerm-win.msi                with -Msi: SicTerm for every user of a PC, into Program Files\Chris Luton\SicTerm, for an
                                 administrator (msiexec /i SicTerm-win.msi /qn, VELOPACK_INSTALLDIR="<folder>" for another
                                 folder); each user keeps their own settings, and the copy does not update itself

The package id is SicTerm, so Setup installs to %LOCALAPPDATA%\SicTerm. It must never be SuperIntelligentChat, SIC's data
folder, which an uninstall would then take with it.

The release is signed with Azure Artifact Signing (docs\decisions\code-signing.md): Publish-App.ps1 signs the identity package,
and vpk signs SicTerm.exe, our other files, Update.exe and the Setup with scripts\artifact-signing.json, leaving files
already signed, such as Microsoft's, as they are. It needs the Azure CLI signed in as an account that may sign, which
scripts\Use-ArtifactSigning.ps1 checks before the build. -Unsigned leaves the app and Setup unsigned for a local trial.

The package id was SIChat until October 13, 2026. vpk takes every package in the output folder for the same app: with SIChat's
packages there it builds a delta from the last of them and lists both ids in releases.win.json. So a folder that holds another
id's packages is refused before the build; move them to a folder of their own, such as artifacts\releases-SIChat.

-Upload creates the GitHub release v<version> in the repository and attaches those files. It needs a clean working
tree whose commit is on origin, so the tag names the code the release was built from, and the personal account's login in
the GitHub CLI (gh auth login as chris-luton); its token goes to vpk and is never printed.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'src\Sic.App\Sic.App.csproj'
$repositoryUrl = 'https://github.com/SicTerm/sicterm'
$account = 'chris-luton'
$packageId = 'SicTerm'
if (-not $Version) { $Version = @(([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ })[0] }
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Not a release version: '$Version'." }
if (-not $OutputDir) { $OutputDir = Join-Path $repo 'artifacts\releases' }
$foreign = @(Get-ChildItem -LiteralPath $OutputDir -Filter *.nupkg -ErrorAction Ignore | Where-Object { -not $_.Name.StartsWith("$packageId-", [StringComparison]::OrdinalIgnoreCase) })
if ($foreign) { throw "$OutputDir holds packages of another package id, such as $($foreign[0].Name). Move them, or the whole folder, to a folder of their own (artifacts\releases-SIChat, say) and build again." }

if ($Upload -and $Unsigned) { throw 'A release is never uploaded unsigned.' }
# Checked before the build, so a missing sign-in stops it before the smoke rather than after it.
$signing = if ($Unsigned) { $null } else { & (Join-Path $PSScriptRoot 'Use-ArtifactSigning.ps1') }

if ($Upload) {
    # Checked before the build, not after it: a release is tagged at the commit it was built from, so that commit must be pushed.
    if (git -C $repo status --porcelain) { throw 'The working tree has changes. Commit and push them first, so the release is tagged at the code it was built from.' }
    $head = (git -C $repo rev-parse HEAD).Trim()
    git -C $repo fetch --quiet origin
    if (-not (git -C $repo branch --remotes --contains $head)) { throw "Commit $head is not on origin. Push it first." }
}

$published = $null
& (Join-Path $PSScriptRoot 'Publish-App.ps1') -Smoke:(-not $SkipSmoke) -ScratchRoot $ScratchRoot -Version $Version -PassThru | ForEach-Object {
    if ($_ -is [pscustomobject] -and $_.PSObject.Properties.Name -contains 'PublishedTo') { $published = $_.PublishedTo } else { $_ | Out-Host }
}
if (-not $published) { throw 'Publish-App.ps1 did not say where it published.' }

Push-Location $repo
try {
    dotnet tool restore | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (exit code $LASTEXITCODE)." }
    # The MSI installs for every user of the PC, into Program Files (an administrator can name another folder with
    # VELOPACK_INSTALLDIR); a copy there leaves its updates to the next MSI (Updates.IsManaged).
    $msiArguments = if ($Msi) { @('--msi', '--instLocation', 'PerMachine') } else { @() }
    $signArguments = if ($signing) { @('--azureTrustedSignFile', $signing.Metadata) } else { @() }
    dotnet vpk pack --packId $packageId --packVersion $Version --packDir $published --mainExe SicTerm.exe --runtime win-x64 `
        --packTitle SicTerm --packAuthors 'Chris Luton' --icon (Join-Path $repo 'src\Sic.App\Assets\brand\sic.ico') --outputDir $OutputDir @msiArguments @signArguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit code $LASTEXITCODE)." }

    if ($Upload) {
        $token = gh auth token --hostname github.com --user $account 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $token) { throw "The GitHub CLI has no login for $account. Run gh auth login and sign in as $account, then build again." }
        dotnet vpk upload github --outputDir $OutputDir --repoUrl $repositoryUrl --token $token --publish --releaseName "SicTerm $Version" --tag "v$Version" --targetCommitish $head | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "vpk upload failed (exit code $LASTEXITCODE)." }
        "Released SicTerm $Version as $repositoryUrl/releases/tag/v$Version"
    }
}
finally { Pop-Location }
"Packed SicTerm $Version into $OutputDir. Install it with $(Join-Path $OutputDir "$packageId-win-Setup.exe")."
