[CmdletBinding()]
param(
    # The released version: its GitHub release v<Version> must already have SicTerm-win-Setup.exe.
    [Parameter(Mandatory)] [string]$Version,
    # The same Setup, to hash; the release's copy is downloaded when not given.
    [string]$Setup,
    # The GitHub account whose fork of microsoft/winget-pkgs the pull request comes from (its login in the GitHub CLI).
    [string]$Account = 'chris-luton',
    # Write and validate the manifests into this folder and stop there, without the pull request.
    [string]$ManifestOnlyTo
)
<#
Opens the pull request that adds SicTerm <version> to winget (microsoft/winget-pkgs), as SicTerm.SicTerm. Writes the three
manifests (version, installer, en-US locale), checks them with winget validate, and commits them to a branch of the account's
fork, which is created when missing and brought up to date with Microsoft's master first. Microsoft's bots then test the
installer in a sandbox and a moderator merges it; winget install SicTerm.SicTerm offers the version a few hours after that.

The installer is the GitHub release's Setup (winget follows the release URL's redirect). It is Velopack's Setup for the current
user: --silent, and an uninstall entry under the key SicTerm whose Publisher is the --packAuthors Build-Release.ps1 passes.
Stops when a pull request for this version is already open. Needs the account's login in the GitHub CLI and winget.
#>
$ErrorActionPreference = 'Stop'
$id = 'SicTerm.SicTerm'
$publisher = 'Christopher Luton'
$repository = 'https://github.com/SicTerm/sicterm'
$installerUrl = "$repository/releases/download/v$Version/SicTerm-win-Setup.exe"
$schema = '1.10.0'

if (-not $Setup) {
    $Setup = Join-Path ([IO.Path]::GetTempPath()) "SicTerm-$Version-Setup.exe"
    Invoke-WebRequest -Uri $installerUrl -OutFile $Setup
}
$hash = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash
$signature = Get-AuthenticodeSignature -LiteralPath $Setup
if ($signature.Status -ne 'Valid') { throw "$Setup is not validly signed ($($signature.Status)); winget's checks would refuse it." }

$head = { param($type) "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.$type.$schema.schema.json`n`nPackageIdentifier: $id`nPackageVersion: $Version`n" }
$files = [ordered]@{
    "$id.yaml" = (& $head 'version') + "DefaultLocale: en-US`nManifestType: version`nManifestVersion: $schema`n"
    "$id.installer.yaml" = (& $head 'installer') + @"
InstallerType: exe
Scope: user
InstallModes:
- interactive
- silent
InstallerSwitches:
  Silent: --silent
  SilentWithProgress: --silent
  InstallLocation: --installto "<INSTALLPATH>"
UpgradeBehavior: install
ProductCode: SicTerm
ReleaseDate: $((Get-Date).ToString('yyyy-MM-dd'))
AppsAndFeaturesEntries:
- DisplayName: SicTerm
  Publisher: $publisher
  ProductCode: SicTerm
Installers:
- Architecture: x64
  InstallerUrl: $installerUrl
  InstallerSha256: $hash
ManifestType: installer
ManifestVersion: $schema

"@
    "$id.locale.en-US.yaml" = (& $head 'defaultLocale') + @"
PackageLocale: en-US
Publisher: $publisher
PublisherUrl: https://github.com/SicTerm
PublisherSupportUrl: $repository/issues
PrivacyUrl: https://sicterm.app/privacy
Author: $publisher
PackageName: SicTerm
PackageUrl: https://sicterm.app
License: MIT
LicenseUrl: $repository/blob/main/LICENSE
ShortDescription: Run and resume Claude Code, Codex, OpenCode and Hermes terminal sessions side by side, and search every conversation they have saved.
Description: |-
  SicTerm (Super-Intelligent-Chat Terminal) runs the coding agents already installed on the PC, signed in with their own accounts, in tabs and split panes next to PowerShell.
  Conversations come from each agent's own saved history, which SicTerm reads but never changes, and resume through the agent itself in the folder they belong to.
  A local search index of that history makes every agent's conversations searchable in one place.
  SicTerm calls no model or AI service itself; its only network request of its own is to GitHub, to check for updates.
Moniker: sicterm
Tags:
- ai
- claude-code
- codex
- coding-agent
- opencode
- terminal
- windows-terminal
ReleaseNotesUrl: $repository/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema

"@
}

$folder = if ($ManifestOnlyTo) { $ManifestOnlyTo } else { Join-Path ([IO.Path]::GetTempPath()) "winget-$id-$Version" }
New-Item -ItemType Directory -Force -Path $folder | Out-Null
foreach ($name in $files.Keys) { [IO.File]::WriteAllText((Join-Path $folder $name), $files[$name].Replace("`r`n", "`n")) }
winget validate --manifest $folder | Out-Host
if ($LASTEXITCODE -ne 0) { throw "winget validate refused the manifests in $folder." }
if ($ManifestOnlyTo) { Write-Host "Manifests for $id $Version are valid, in $folder."; return }

$token = gh auth token --hostname github.com --user $Account 2>$null
if ($LASTEXITCODE -ne 0 -or -not $token) { throw "The GitHub CLI has no login for $Account. Run gh auth login and sign in as $Account." }
$env:GH_TOKEN = $token
try {
    $title = "New version: $id version $Version"
    $open = gh pr list --repo microsoft/winget-pkgs --state open --search "$id $Version in:title" --json url --jq '.[0].url'
    if ($open) { Write-Host "A pull request for $id $Version is already open: $open"; return $open }
    $fork = "$Account/winget-pkgs"
    gh repo fork microsoft/winget-pkgs --clone=false 2>&1 | Out-Null
    gh api -X POST "repos/$fork/merge-upstream" -f branch=master | Out-Null
    $base = gh api "repos/$fork/git/ref/heads/master" --jq .object.sha
    $tree = gh api "repos/$fork/git/commits/$base" --jq .tree.sha
    $path = "manifests/s/SicTerm/SicTerm/$Version"
    $treeFile = Join-Path $folder 'tree.json'
    @{ base_tree = $tree; tree = @($files.Keys | ForEach-Object { @{ path = "$path/$_"; mode = '100644'; type = 'blob'; content = $files[$_].Replace("`r`n", "`n") } }) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $treeFile -Encoding utf8NoBOM
    $newTree = gh api "repos/$fork/git/trees" --input $treeFile --jq .sha
    $commit = gh api "repos/$fork/git/commits" -f message=$title -f tree=$newTree -f "parents[]=$base" --jq .sha
    $branch = "$id-$Version"
    gh api "repos/$fork/git/refs" -f ref="refs/heads/$branch" -f sha=$commit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Creating the branch $branch in $fork failed; it may already exist." }
    $body = @"
## 📖 Description

$title. Installer: Velopack Setup for the current user (``--silent``), signed with Azure Artifact Signing ($publisher). Source: $repository

## 📦 Manifest Checklist

- [x] Checked that there aren't other open pull requests for the same manifest update/change
- [x] This PR only modifies one (1) manifest
- [x] Validated manifest locally with ``winget validate --manifest <path>``
"@
    $url = gh pr create --repo microsoft/winget-pkgs --head "${Account}:$branch" --base master --title $title --body $body
    if ($LASTEXITCODE -ne 0) { throw 'gh pr create failed.' }
    Write-Host "Opened $url"
    $url
}
finally { Remove-Item Env:\GH_TOKEN -ErrorAction Ignore }
