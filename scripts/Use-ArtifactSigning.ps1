[CmdletBinding()]
param()
<#
Gets this PC ready to sign with Azure Artifact Signing (docs/decisions/code-signing.md) and outputs what a signature needs:
Metadata, the account and certificate profile (scripts\artifact-signing.json); SignTool and Dlib, the SignTool and Artifact
Signing client that vpk carries (vendor\signing in the vpk tool pinned in dotnet-tools.json); and Arguments, SignTool's
arguments to sign with them, timestamped by Microsoft.

The signing client signs as whoever the Azure CLI is signed in as, who must hold the Artifact Signing Certificate Profile
Signer role on the account. Throws when the Azure CLI is missing or not signed in, or vpk is not restored.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$metadata = Join-Path $PSScriptRoot 'artifact-signing.json'

# The Azure CLI's installer puts it on PATH for new sessions only; a session started before the install finds it here.
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    $cli = Join-Path $env:ProgramFiles 'Microsoft SDKs\Azure\CLI2\wbin'
    if (-not (Test-Path -LiteralPath (Join-Path $cli 'az.cmd'))) { throw 'The Azure CLI is not installed (winget install -e --id Microsoft.AzureCLI). Signing needs it, signed in with az login.' }
    $env:PATH = "$cli;$env:PATH"
}
$null = az account show --only-show-errors 2>$null
if ($LASTEXITCODE -ne 0) { throw "The Azure CLI is not signed in. Run az login --tenant <the signing account's directory>, as the account that may sign with $metadata." }

$vpk = (Get-Content -LiteralPath (Join-Path $repo 'dotnet-tools.json') -Raw | ConvertFrom-Json).tools.vpk.version
$vendor = Join-Path $env:USERPROFILE ".nuget\packages\vpk\$vpk\vendor\signing"
if (-not (Test-Path -LiteralPath (Join-Path $vendor 'Azure.CodeSigning.Dlib.dll'))) {
    Push-Location $repo
    try { dotnet tool restore | Out-Null } finally { Pop-Location }
    if (-not (Test-Path -LiteralPath (Join-Path $vendor 'Azure.CodeSigning.Dlib.dll'))) { throw "vpk $vpk's signing client is not at $vendor." }
}
$dlib = Join-Path $vendor 'Azure.CodeSigning.Dlib.dll'
[pscustomobject]@{
    Metadata  = $metadata
    SignTool  = Join-Path $vendor 'signtool.exe'
    Dlib      = $dlib
    Arguments = @('sign', '/fd', 'SHA256', '/tr', 'http://timestamp.acs.microsoft.com', '/td', 'SHA256', '/dlib', $dlib, '/dmdf', $metadata)
}
