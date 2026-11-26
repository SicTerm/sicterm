[CmdletBinding()]
param(
    # The release's version: the file goes to v<Version>/ in the bucket.
    [Parameter(Mandatory)] [string]$Version,
    # The signed SicTerm-win-Setup.exe to publish.
    [Parameter(Mandatory)] [string]$Setup,
    # Only check that the credentials are set; Build-Release.ps1 does this before the build, so a missing one stops it early.
    [switch]$CheckOnly
)
<#
Publishes Setup to https://download.sicterm.app/v<version>/SicTerm-win-Setup.exe, a Cloudflare R2 bucket on SicTerm's domain, and
outputs that address. The Microsoft Store installs SicTerm from it: Partner Center takes an installer URL only when it answers
the file itself, and a GitHub release asset answers with a redirect. The file at an address must never change once the Store
has it, so a version already there is left as it is when it is the same file, and refused when it is not.

It uploads with the S3 API that R2 offers, through the curl.exe that Windows ships (--aws-sigv4), and needs an R2 API token for
the bucket in three environment variables, kept out of the repository and out of the command line:

  SICTERM_R2_ACCOUNT_ID         the Cloudflare account's ID
  SICTERM_R2_ACCESS_KEY_ID      the token's Access Key ID
  SICTERM_R2_SECRET_ACCESS_KEY  the token's Secret Access Key

Create the token in Cloudflare (R2 → Manage API tokens) with Object Read & Write on the sicterm-downloads bucket only. After the
upload the file is downloaded from its public address, which must answer without a redirect, and its SHA-256 compared with Setup's.
#>
$ErrorActionPreference = 'Stop'
$bucket = 'sicterm-downloads'
$publicBase = 'https://download.sicterm.app'
$names = 'SICTERM_R2_ACCOUNT_ID', 'SICTERM_R2_ACCESS_KEY_ID', 'SICTERM_R2_SECRET_ACCESS_KEY'
$missing = @($names | Where-Object { -not [Environment]::GetEnvironmentVariable($_) })
if ($missing) { throw "Publishing to download.sicterm.app needs an R2 API token in $($missing -join ', '). See scripts\Publish-Download.ps1." }
$curl = Join-Path $env:SystemRoot 'System32\curl.exe'
if (-not (Test-Path -LiteralPath $curl)) { throw "Windows' curl.exe is not at $curl." }
if ($CheckOnly) { return }

$Setup = (Resolve-Path -LiteralPath $Setup).Path
$key = "v$Version/SicTerm-win-Setup.exe"
$public = "$publicBase/$key"
$hash = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash

function Get-PublicHash {
    # The public file's SHA-256, or null when it is not there; throws when the address redirects.
    $file = Join-Path ([IO.Path]::GetTempPath()) ('sicterm-download-' + [guid]::NewGuid().ToString('N'))
    try {
        $answer = & $curl --silent --show-error --output $file --write-out '%{http_code} %{num_redirects}' $public
        if ($LASTEXITCODE -ne 0) { throw "Downloading $public failed (curl exit code $LASTEXITCODE)." }
        $code, $redirects = $answer -split ' '
        if ($code -eq '404') { return $null }
        if ($code -ne '200') { throw "$public answered HTTP $code." }
        if ($redirects -ne '0') { throw "$public redirects; the Store needs an address that answers the file itself." }
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    }
    finally { Remove-Item -LiteralPath $file -ErrorAction Ignore }
}

$existing = Get-PublicHash
if ($existing -eq $hash) { Write-Host "$public is already this Setup; left as it is." ; return $public }
if ($existing) { throw "$public already holds a different file. A Store submission may point at it, so it is never replaced; publish under a new version." }

# The credentials reach curl through its standard input as a config file, never as an argument another process could read.
$endpoint = "https://$($env:SICTERM_R2_ACCOUNT_ID).r2.cloudflarestorage.com/$bucket/$key"
"user = `"$($env:SICTERM_R2_ACCESS_KEY_ID):$($env:SICTERM_R2_SECRET_ACCESS_KEY)`"" |
    & $curl --silent --show-error --fail-with-body --config - --aws-sigv4 'aws:amz:auto:s3' --upload-file $Setup `
        --header 'Content-Type: application/x-msdownload' $endpoint | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Uploading $key to R2 failed (curl exit code $LASTEXITCODE)." }

$published = Get-PublicHash
if ($published -ne $hash) { throw "$public does not match Setup after the upload (SHA-256 $published, expected $hash)." }
Write-Host "Published $public (SHA-256 $hash)."
$public
