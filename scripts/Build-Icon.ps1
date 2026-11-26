[CmdletBinding()]
param(
    [string]$BrandDir = (Join-Path $PSScriptRoot '..\src\Sic.App\Assets\brand'),
    [string]$FullSvg = 'sic.svg',
    [string]$SmallSvg = 'sic-small.svg',
    [int[]]$PngSizes = @(16, 20, 24, 28, 30, 32, 36, 40, 42, 48, 56, 64, 256),
    [int[]]$IcoSizes = @(16, 20, 24, 28, 30, 32, 36, 40, 42, 48, 56, 64, 256),
    [int]$SmallUpTo = 32,
    [string]$FrameSvg = 'sic-{0}.svg',
    [string]$PixelMap = 'sic-{0}.pixels',
    # The .ico and the PNG frames are written as <Name>.ico and <Name>-<n>.png.
    [string]$Name = 'sic'
)
<#
Rebuilds sic.ico and the sic-<n>.png frames (or <Name>.ico and its frames). Each frame comes from the first of these that exists:
- a hand-pixelled map (-PixelMap, e.g. sic-16.pixels), taken pixel for pixel;
- an SVG drawn on that size's own pixel grid (-FrameSvg, e.g. sic-24.svg): the tray, title-bar and taskbar frames, 16 to
  48 px, fitted to the grid as icon-branding-runway docs/icon-suite-spec.md §3 asks;
- a brand SVG: the small cut at or below -SmallUpTo, the full cut above it.
Every SVG is rendered anti-aliased at exactly its frame's size, by headless Microsoft Edge on a transparent background,
never scaled down from a larger render: a frame scaled down reads soft (the 48 px frame did, with 64% of its edge pixels
in a soft ramp). The frames are packed into an .ico with PNG-compressed entries, 16 to 256 px: every size the tray, the
title bar, the taskbar button and Start pins ask for from 100% to 200% scaling. Run after changing a frame, a map or a
brand SVG, then check the result with Icon Branding Runway:  runway check src\Sic.App\Assets\brand

The shipped icon comes in two sets: sic.ico, the white >_ for dark taskbars, which is also the exe's icon, and
sic-light.ico, the near-black >_ for light ones, which the app hands the taskbar, title bar and tray when the taskbar is light:
  .\scripts\Build-Icon.ps1
  .\scripts\Build-Icon.ps1 -Name sic-light -FrameSvg 'sic-light-{0}.svg' -FullSvg sic-light.svg -SmallSvg sic-light.svg
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$BrandDir = (Resolve-Path -LiteralPath $BrandDir).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('sic-icon-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

# A map is "palette" lines (<key> <#RRGGBB or #RRGGBBAA>), then "pixels" and one row of keys per pixel row; "#" starts a
# comment. Keys are compared by character, so upper and lower case are different colours.
function Read-PixelMap([string]$path, [int]$size) {
    $palette = @{}; $rows = [System.Collections.Generic.List[string]]::new(); $section = ''
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match '^\s*(#|$)') { continue }
        if ($line -in 'palette', 'pixels') { $section = $line; continue }
        if ($section -eq 'palette') { $key, $hex = $line.Trim() -split '\s+', 2; $palette[[int][char]$key] = [System.Drawing.ColorTranslator]::FromHtml('#' + $hex.TrimStart('#').Substring(0, 6)), $(if ($hex.TrimStart('#').Length -eq 8) { [Convert]::ToInt32($hex.TrimStart('#').Substring(6, 2), 16) } else { 255 }) }
        elseif ($section -eq 'pixels') { $rows.Add($line) }
    }
    if ($rows.Count -ne $size) { throw "$path has $($rows.Count) rows, not $size." }
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $size; $y++) {
        if ($rows[$y].Length -ne $size) { throw "$path row $($y + 1) has $($rows[$y].Length) pixels, not $size." }
        for ($x = 0; $x -lt $size; $x++) {
            $entry = $palette[[int]$rows[$y][$x]]
            if (-not $entry) { throw "$path row $($y + 1) column $($x + 1) uses '$($rows[$y][$x])', which is not in its palette." }
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($entry[1], $entry[0]))
        }
    }
    return $bitmap
}

# Renders every SVG frame in one pass: each is laid out at 1:1 on one transparent page, at a whole-pixel position, the
# page is captured once, and each frame is cut out of the capture. $jobs maps a size to the SVG drawn at it.
function Render-Frames([hashtable]$jobs) {
    $edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
        Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $edge) { throw 'Microsoft Edge was not found; it is what renders the SVGs.' }
    $x = 0; $placed = foreach ($size in ($jobs.Keys | Sort-Object)) { @{ Size = $size; X = $x; Svg = $jobs[$size] }; $x += $size + 8 }
    $width = $x; $height = ($jobs.Keys | Measure-Object -Maximum).Maximum
    $images = ($placed | ForEach-Object { "<img src=`"$(([uri](Join-Path $BrandDir $_.Svg)).AbsoluteUri)`" width=`"$($_.Size)`" height=`"$($_.Size)`" style=`"position:absolute;left:$($_.X)px;top:0`">" }) -join ''
    $html = Join-Path $work 'frames.html'
    "<!doctype html><html><body style=`"margin:0;background:transparent`">$images</body></html>" | Set-Content -LiteralPath $html -Encoding UTF8
    $png = Join-Path $work 'frames.png'
    # Headless Edge sometimes hangs on a screenshot for no reason found yet. A new profile folder for every launch and a
    # virtual time budget make it finish; a launch that still hangs is stopped after 90 s and retried once on a new folder.
    foreach ($attempt in 1, 2) {
        $arguments = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000', "--window-size=$width,$height",
            '--force-device-scale-factor=1', '--virtual-time-budget=15000', "--screenshot=$png",
            "--user-data-dir=$(Join-Path $work ('profile-' + [guid]::NewGuid().ToString('N')))", ([uri]$html).AbsoluteUri)
        $process = Start-Process -FilePath $edge -ArgumentList $arguments -PassThru -WindowStyle Hidden
        if ($process.WaitForExit(90000)) { break }
        $process.Kill($true)
        if ($attempt -eq 2) { throw "Edge did not finish rendering the frames in 90 s, twice." }
    }
    if (-not (Test-Path -LiteralPath $png)) { throw "Edge did not write $png (exit code $($process.ExitCode))." }
    $capture = [System.Drawing.Bitmap]::FromFile($png)
    try {
        if ($capture.Width -ne $width -or $capture.Height -ne $height) { throw "The capture is $($capture.Width) x $($capture.Height), not $width x ${height}: the page was not drawn at 1:1." }
        $gap = $capture.GetPixel($placed[0].Size + 4, $height - 1)
        if ($gap.A -ne 0) { throw "The capture is opaque between frames (alpha $($gap.A)); the transparent background flag was not honoured." }
        $frames = @{}
        foreach ($p in $placed) {
            $frames[$p.Size] = $capture.Clone([System.Drawing.Rectangle]::new($p.X, 0, $p.Size, $p.Size), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        }
        return $frames
    }
    finally { $capture.Dispose() }
}

$sources = @{}; $jobs = @{}
foreach ($size in (($PngSizes + $IcoSizes) | Sort-Object -Unique)) {
    $map = Join-Path $BrandDir ($PixelMap -f $size)
    $frame = $FrameSvg -f $size
    if (Test-Path -LiteralPath $map) { $sources[$size] = Split-Path $map -Leaf }
    elseif (Test-Path -LiteralPath (Join-Path $BrandDir $frame)) { $sources[$size] = $frame; $jobs[$size] = $frame }
    else { $sources[$size] = if ($size -le $SmallUpTo) { $SmallSvg } else { $FullSvg }; $jobs[$size] = $sources[$size] }
}
$rendered = if ($jobs.Count) { Render-Frames $jobs } else { @{} }
$frames = @{}
foreach ($size in $sources.Keys) {
    $bitmap = if ($rendered.ContainsKey($size)) { $rendered[$size] } else { Read-PixelMap (Join-Path $BrandDir $sources[$size]) $size }
    $stream = [IO.MemoryStream]::new(); $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
    $frames[$size] = $stream.ToArray()
    if ($PngSizes -contains $size) { [IO.File]::WriteAllBytes((Join-Path $BrandDir "$Name-$size.png"), $frames[$size]) }
    Write-Verbose "$size px from $($sources[$size])"
}

# ICO layout: 6-byte header, a 16-byte directory entry per frame, then the PNG payloads in the same order.
$ico = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($ico)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$IcoSizes.Count)
$offset = 6 + 16 * $IcoSizes.Count
foreach ($size in $IcoSizes) {
    $bytes = $frames[$size]
    $dimension = [byte]$(if ($size -ge 256) { 0 } else { $size })
    $writer.Write($dimension); $writer.Write($dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$bytes.Length); $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($size in $IcoSizes) { $writer.Write($frames[$size]) }
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $BrandDir "$Name.ico"), $ico.ToArray())
[IO.Directory]::Delete($work, $true)
"Wrote $Name.ico ($($ico.Length) bytes; frames $($IcoSizes -join ', ')) and PNG frames $($PngSizes -join ', ') to $BrandDir"
