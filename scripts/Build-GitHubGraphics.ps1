[CmdletBinding()]
param(
    [string]$BrandDir = (Join-Path $PSScriptRoot '..\src\Sic.App\Assets\brand'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\docs\brand\github'),
    [string]$Tagline = 'Every coding agent in one window. Every conversation searchable, and resumed exactly where it was.'
)
<#
Builds SicTerm's GitHub graphics from the shipped mark (sic.svg, simple-25 A), on SicTerm Dark's base colour (#01263E), with
the wordmark in the shipped fonts (Assets\fonts: Sora for "Sic", Azeret Mono Light for "Term"):
- avatar.png, 1024 x 1024: the organization's (or an account's) profile picture. GitHub rounds an organization's corners
  and an account's to a circle, so the mark stays inside the inscribed circle.
- social-preview.png, 1280 x 640: a repository's Settings -> General -> Social preview (GitHub asks for at least
  640 x 320, under 1 MB). Link previews crop its edges, so the mark and the words keep a wide margin.
- mark-ondark.svg and mark-onlight.svg: the mark for a README, in a <picture> that follows GitHub's colour mode.
Each PNG is rendered at exactly its size by headless Microsoft Edge, as Build-Icon.ps1 renders the icon's frames.
  .\scripts\Build-GitHubGraphics.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$BrandDir = (Resolve-Path -LiteralPath $BrandDir).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('sic-github-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$mark = ([uri](Join-Path $BrandDir 'sic.svg')).AbsoluteUri
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found; it is what renders the graphics.' }

# SicTerm Dark's base, lifted a little behind the mark, so the image reads as the app's own surface rather than a flat fill.
$background = 'background:radial-gradient(circle at {0} 50%, #00385A 0%, #01263E {1}%)'
$fonts = Join-Path $BrandDir '..\fonts'
$fontFaces = "<style>@font-face{font-family:'Sora';src:url('$(([uri](Join-Path $fonts 'Sora.ttf')).AbsoluteUri)');font-weight:100 800}" +
    "@font-face{font-family:'Azeret Mono';src:url('$(([uri](Join-Path $fonts 'AzeretMono.ttf')).AbsoluteUri)');font-weight:100 900}</style>"

function Render-Page([string]$file, [int]$width, [int]$height, [string]$body) {
    $html = Join-Path $work ([IO.Path]::GetFileNameWithoutExtension($file) + '.html')
    "<!doctype html><html><head><meta charset=`"utf-8`">$fontFaces</head><body style=`"margin:0;width:${width}px;height:${height}px;overflow:hidden`">$body</body></html>" |
        Set-Content -LiteralPath $html -Encoding UTF8
    $png = Join-Path $OutDir $file
    Remove-Item -LiteralPath $png -ErrorAction SilentlyContinue
    # As in Build-Icon.ps1: a new profile folder for every launch and a virtual time budget keep headless Edge from hanging.
    foreach ($attempt in 1, 2) {
        $arguments = @('--headless=new', '--disable-gpu', '--hide-scrollbars', "--window-size=$width,$height",
            '--force-device-scale-factor=1', '--virtual-time-budget=15000', "--screenshot=$png",
            "--user-data-dir=$(Join-Path $work ('profile-' + [guid]::NewGuid().ToString('N')))", ([uri]$html).AbsoluteUri)
        $process = Start-Process -FilePath $edge -ArgumentList $arguments -PassThru -WindowStyle Hidden
        if ($process.WaitForExit(90000)) { break }
        $process.Kill($true)
        if ($attempt -eq 2) { throw "Edge did not finish rendering $file in 90 s, twice." }
    }
    if (-not (Test-Path -LiteralPath $png)) { throw "Edge did not write $png (exit code $($process.ExitCode))." }
    $image = [System.Drawing.Image]::FromFile($png)
    try { if ($image.Width -ne $width -or $image.Height -ne $height) { throw "$file is $($image.Width) x $($image.Height), not $width x $height." } }
    finally { $image.Dispose() }
    if ((Get-Item -LiteralPath $png).Length -ge 1MB) { throw "$file is over 1 MB, GitHub's limit." }
    Write-Host "$file  $width x $height  $([math]::Round((Get-Item -LiteralPath $png).Length / 1KB)) KB"
}

try {
    # The mark at 70%: its float shadow included, it stays inside the circle GitHub cuts an account's picture to.
    Render-Page 'avatar.png' 1024 1024 ("<div style=`"width:1024px;height:1024px;display:flex;align-items:center;justify-content:center;" +
        ($background -f '50%', 70) + "`"><img src=`"$mark`" width=`"716`" height=`"716`"></div>")

    $font = "font-family:'Segoe UI Variable Display','Segoe UI',sans-serif"
    Render-Page 'social-preview.png' 1280 640 ("<div style=`"width:1280px;height:640px;display:flex;align-items:center;box-sizing:border-box;padding:0 112px;gap:64px;" +
        ($background -f '22%', 75) + "`"><img src=`"$mark`" width=`"300`" height=`"300`" style=`"flex:none`">" +
        "<div style=`"$font`"><div style=`"color:#FFFFFF;font-size:112px;line-height:1;display:flex;align-items:baseline`">" +
        "<span style=`"font-family:'Sora';font-weight:800;letter-spacing:-2px`">Sic</span>" +
        "<span style=`"font-family:'Azeret Mono';font-weight:300;font-size:1.046em;color:#009DC1`">Term</span></div>" +
        "<div style=`"color:#FFFFFFB3;font-size:34px;line-height:1.35;margin-top:28px;max-width:640px`">$Tagline</div>" +
        "<div style=`"color:#2FB6D4;font-size:26px;font-weight:600;margin-top:32px`">for Windows</div></div></div>")

    Copy-Item -LiteralPath (Join-Path $BrandDir 'sic.svg') -Destination (Join-Path $OutDir 'mark-ondark.svg') -Force
    Copy-Item -LiteralPath (Join-Path $BrandDir 'sic-light.svg') -Destination (Join-Path $OutDir 'mark-onlight.svg') -Force
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
