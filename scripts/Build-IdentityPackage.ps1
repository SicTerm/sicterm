[CmdletBinding()]
param(
    # The published folder: the package's external location, where SicTerm.Identity.msix is written.
    [Parameter(Mandatory)] [string]$PublishedTo,
    # SIC's version (<Version>, or Build-Release's -Version); a prerelease suffix is dropped, since a package version is four numbers.
    [Parameter(Mandatory)] [string]$Version,
    # The signing certificate's subject; the Publisher in the manifests must be exactly this. Artifact Signing's certificate
    # profile issues its certificates with this subject (docs/decisions/code-signing.md).
    [string]$Subject = 'CN=Christopher Luton, O=Christopher Luton, L=Austin, S=tx, C=US'
)
<#
Builds and signs SIC's identity package (docs/decisions/explorer-menu.md): src\Sic.App\Package\AppxManifest.xml with the version
filled in, packed with MakeAppx /nv (it holds no files; the published folder is its external location) and signed with Azure
Artifact Signing (scripts\Use-ArtifactSigning.ps1), whose certificate's subject is -Subject. MakeAppx comes from the
Microsoft.Windows.SDK.BuildTools package the app restores. The package manifest's Identity and ExplorerMenu's constants
in Sic.Core must agree, and app.manifest must carry no msix element (SicTerm.exe runs without identity); the script stops otherwise.

Writes <PublishedTo>\SicTerm.Identity.msix and outputs its path. Throws when the Azure CLI is not signed in as an account that
may sign, or when the signature is not the certificate -Subject names.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $repo 'src\Sic.App\Package\AppxManifest.xml'
$appManifestPath = Join-Path $repo 'src\Sic.App\app.manifest'
$corePath = Join-Path $repo 'src\Sic.Core\Shell\ExplorerMenu.cs'

$signing = & (Join-Path $PSScriptRoot 'Use-ArtifactSigning.ps1')

# One identity, two places: the package manifest and the constants SIC registers with. app.manifest must carry no msix element:
# with one, SicTerm.exe ran as the package's activated application and Windows ended it with the package's app container.
$manifest = [xml](Get-Content -LiteralPath $manifestPath -Raw)
$identity = $manifest.Package.Identity
$appManifest = [xml](Get-Content -LiteralPath $appManifestPath -Raw)
if ($appManifest.assembly.msix) { throw 'app.manifest has an msix element; SicTerm.exe must not take the package identity (docs/decisions/explorer-menu.md).' }
$core = Get-Content -LiteralPath $corePath -Raw
foreach ($check in @(
    @{ What = "the package manifest's Publisher"; Value = $identity.Publisher; Expected = $Subject },
    @{ What = "ExplorerMenu.PackagePublisher"; Value = [regex]::Match($core, 'PackagePublisher = "([^"]+)"').Groups[1].Value; Expected = $Subject },
    @{ What = "ExplorerMenu.PackageName"; Value = [regex]::Match($core, 'PackageName = "([^"]+)"').Groups[1].Value; Expected = $identity.Name },
    @{ What = "ExplorerMenu.PackageApplicationId"; Value = [regex]::Match($core, 'PackageApplicationId = "([^"]+)"').Groups[1].Value; Expected = $manifest.Package.Applications.Application.Id }
)) {
    if ($check.Value -cne $check.Expected) { throw "$($check.What) is '$($check.Value)', not '$($check.Expected)'. The identity must be the same everywhere." }
}

$numbers = ($Version -split '-')[0] -split '\.'
while ($numbers.Count -lt 4) { $numbers += '0' }
$packageVersion = ($numbers | Select-Object -First 4) -join '.'

$tools = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') -Directory | Sort-Object Name -Descending | ForEach-Object {
    Get-ChildItem (Join-Path $_.FullName 'bin') -Directory | Sort-Object Name -Descending | Select-Object -First 1
} | Select-Object -First 1
if (-not $tools) { throw 'The Windows SDK build tools (Microsoft.Windows.SDK.BuildTools) are not in the NuGet cache; restore the app first.' }
$makeappx = Join-Path $tools.FullName 'x64\makeappx.exe'

$work = Join-Path ([IO.Path]::GetTempPath()) ('sic-identity-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    (Get-Content -LiteralPath $manifestPath -Raw).Replace('SIC_PACKAGE_VERSION', $packageVersion) | Set-Content -LiteralPath (Join-Path $work 'AppxManifest.xml') -Encoding utf8NoBOM
    $package = Join-Path $PublishedTo 'SicTerm.Identity.msix'
    if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Force }
    & $makeappx pack /o /d $work /nv /p $package | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed (exit code $LASTEXITCODE)." }
    $arguments = $signing.Arguments + $package
    & $signing.SignTool @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed (exit code $LASTEXITCODE)." }
    $signer = (Get-AuthenticodeSignature -LiteralPath $package).SignerCertificate
    if ($signer.Subject -cne $Subject) { throw "SicTerm.Identity.msix was signed by '$($signer.Subject)', not '$Subject'." }
    $package
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
