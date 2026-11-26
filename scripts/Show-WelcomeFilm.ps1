[CmdletBinding()]
param(
    # A built-in theme to show the film under: a file name under src\Sic.Core\Appearance\Themes, with or without its number and
    # extension (08-navy-light, navy-light, garnet-dark). Without it the page's own colors, SicTerm Dark.
    [string]$Theme,
    # Open the film as the app shows it, without the scrub toolbar.
    [switch]$NoScrub
)
<#
Opens the welcome film (src\Sic.App\Assets\welcome\welcome.html, docs/decisions/first-run.md) in Microsoft Edge in scrub mode:
a toolbar under the film plays, pauses, speeds, scrubs and jumps between its beats, and a chime cue stands in for the window's
chime. A cut of the film is iterated here, by editing the page and reloading (F5 in the window), not through a publish: the
publish smoke is for the host (MainWindow.Welcome.cs, WelcomeView.cs) and for a release.

Edge is started with file access allowed, so the page's fonts load from the repository, in a window of its own with a profile
of its own under %TEMP%\sicterm-welcome, so the flag takes while Edge is otherwise open. -Theme writes a copy of the page there
that stands in for the app and sends the theme's colors, as MainWindow.Welcome.cs does; without a theme the page itself opens.
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $repo 'src\Sic.App\Assets'
$page = Join-Path $assets 'welcome\welcome.html'
$scratch = Join-Path $env:TEMP 'sicterm-welcome'
New-Item -ItemType Directory -Force $scratch | Out-Null
$target = $page

if ($Theme) {
    $themes = Join-Path $repo 'src\Sic.Core\Appearance\Themes'
    $file = Get-ChildItem $themes -Filter '*.json' | Where-Object { $_.BaseName -eq $Theme -or $_.BaseName -match ('^\d+-' + [regex]::Escape($Theme) + '$') } | Select-Object -First 1
    if (-not $file) { throw "No theme '$Theme' under $themes. Themes: $((Get-ChildItem $themes -Filter '*.json' | ForEach-Object { $_.BaseName -replace '^\d+-', '' }) -join ', ')." }
    $def = Get-Content $file.FullName -Raw | ConvertFrom-Json
    $c = $def.chrome
    function Rgb([string]$hex) {
        $n = [Convert]::ToInt32($hex.Substring(1, 6), 16)
        $a = if ($hex.Length -eq 9) { [Convert]::ToInt32($hex.Substring(7, 2), 16) / 255.0 } else { 1.0 }
        [pscustomobject]@{ R = $n -shr 16; G = ($n -shr 8) -band 255; B = $n -band 255; A = $a }
    }
    function Hex($rgb) { '#{0:X2}{1:X2}{2:X2}' -f [int]$rgb.R, [int]$rgb.G, [int]$rgb.B }
    function Rgba([string]$hex) { $v = Rgb $hex; 'rgba({0},{1},{2},{3})' -f $v.R, $v.G, $v.B, ([math]::Round($v.A, 3).ToString([cultureinfo]::InvariantCulture)) }
    # A translucent rail or panel composed over the base, as Theme.Over does for the page
    function Over([string]$hex, [string]$baseHex) {
        $t = Rgb $hex; $b = Rgb $baseHex
        [pscustomobject]@{ R = [math]::Round($t.R * $t.A + $b.R * (1 - $t.A)); G = [math]::Round($t.G * $t.A + $b.G * (1 - $t.A)); B = [math]::Round($t.B * $t.A + $b.B * (1 - $t.A)); A = 1.0 }
    }
    function Lum($rgb) {
        $f = { param($v) $v = $v / 255.0; if ($v -le 0.03928) { $v / 12.92 } else { [math]::Pow(($v + 0.055) / 1.055, 2.4) } }
        0.2126 * (& $f $rgb.R) + 0.7152 * (& $f $rgb.G) + 0.0722 * (& $f $rgb.B)
    }
    $rail = Over $c.rail $c.base
    $panel = Over $c.panel $c.base
    # The strip ink the real strip picks: the rail text on a dark rail, the text on a light one (Theme.StripText)
    $stripText = if ((Lum $rail) -lt 0.4) { Rgba $c.railText } else { Rgba $c.text }
    # Not $theme: PowerShell's variables ignore case, and $Theme is the string parameter, which would turn the table into text
    $colors = [ordered]@{
        base = Hex (Rgb $c.base); rail = Hex $rail; panel = Hex $panel; layer = Rgba $c.layer; hover = Rgba $c.hover
        stroke = Rgba $c.stroke; strokeStrong = Rgba $c.strokeStrong; text = Rgba $c.text; textSecondary = Rgba $c.textSecondary; textTertiary = Rgba $c.textTertiary; stripText = $stripText
        accent = Hex (Rgb $c.accent); accentText = Hex (Rgb $c.accentText); accentSoft = Rgba $c.accentSoft
        success = Hex (Rgb $c.success); warning = Hex (Rgb $c.warning); danger = Hex (Rgb $c.danger); displayFont = $def.displayFont; name = $def.name
    }
    if ($def.agents) {
        foreach ($id in 'hermes', 'claude', 'codex', 'opencode') {
            $hue = $def.agents.$id
            if ($hue) { $colors[$id] = if ($hue -is [string]) { $hue } else { $hue.hue } }
        }
    }
    $json = ConvertTo-Json -InputObject $colors -Compress
    $fonts = 'file:///' + ((Join-Path $assets 'fonts') -replace '\\', '/') + '/'
    $html = Get-Content $page -Raw
    $html = $html.Replace('url("../fonts/', 'url("' + $fonts)
    $html = $html.Replace('<head>', '<head><script>window.chrome = { webview: { postMessage() {}, addEventListener(type, fn) { window.__host = fn; } } };</script>')
    $html = $html.Replace('</body>', "<script>window.__host({ data: { type: 'init', theme: $json } });</script></body>")
    $target = Join-Path $scratch ($file.BaseName + '.html')
    Set-Content $target $html -Encoding UTF8 -NoNewline
}

$url = 'file:///' + ($target -replace '\\', '/') + $(if ($NoScrub) { '' } else { '#scrub' })
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($edge) {
    Start-Process $edge -ArgumentList @('--allow-file-access-from-files', "--user-data-dir=$(Join-Path $scratch 'edge-profile')", '--no-first-run', '--window-size=1100,860', "--app=$url")
} else {
    Write-Warning 'Microsoft Edge was not found; the default browser opens the page, and may not load its fonts from a file.'
    Start-Process $url
}
Write-Host "Opened $url"
