[CmdletBinding()]
param(
    [string]$IconsDir = (Join-Path $PSScriptRoot '..\src\Sic.App\Assets\icons')
)
<#
Draws <agent>.ico, SIC's letter tile for each agent, for File Explorer's Open in SIC submenu (docs/decisions/explorer-menu.md
and agent-icons.md), and opencode.png, OpenCode's logo for its tile in the app. Otherwise SIC ships no agent's logo: in the app
a tile is the tool's own icon, read from the user's install, or a letter on the agent's hue; the menu shows the tool's exe
where its icon is in one, and this tile otherwise. The tile is the agent's hue from the light themes (ThemeDefinition.AgentHue
in Sic.Core), opaque so it reads on light and dark menus alike, with the letter (AgentIcons.Letter) in white. Each frame is
drawn at its own size (16, 20, 24, 32 and 48 px: the menu asks for 16 px at 100 % scaling and 20 or 24 above it), not resampled
from a larger one. Frames are classic 32-bit DIBs, not PNG as sic.ico's are, because the resource compiler that builds them
into SicExplorerCommand.dll takes PNG frames only at 256 px.
Rerun after a hue or a letter changes:  .\scripts\Build-AgentIcons.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$IconsDir = (Resolve-Path -LiteralPath $IconsDir).Path

# id = hue, letter. Keep in step with ThemeDefinition.AgentHue (its light shade) and AgentIcons.Letter.
$Agents = [ordered]@{
    hermes   = @('9A6700', 'H')
    claude   = @('B5502A', 'C')
    codex    = @('0F7A6B', 'X')
    opencode = @('6E4BB8', 'O')
}

function Tile([string]$hue, [string]$letter, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $radius = [Math]::Max(3.0, $size / 5.0)
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $d = 2 * $radius; $edge = $size - 0.5
        $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($edge - $d, 0, $d, $d, 270, 90)
        $path.AddArc($edge - $d, $edge - $d, $d, $d, 0, 90); $path.AddArc(0, $edge - $d, $d, $d, 90, 90); $path.CloseFigure()
        $fill = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#' + $hue))
        $graphics.FillPath($fill, $path)
        $font = [System.Drawing.Font]::new('Segoe UI', [float]($size * 0.6), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $format = [System.Drawing.StringFormat]::new(); $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
        $box = [System.Drawing.RectangleF]::new(0, [float]($size * 0.03), $size, $size)
        $graphics.DrawString($letter, $font, [System.Drawing.Brushes]::White, $box, $format)
        $fill.Dispose(); $font.Dispose(); $format.Dispose(); $path.Dispose()
    }
    finally { $graphics.Dispose() }
    $bitmap
}

# A classic icon frame: a 32-bit BGRA DIB (BITMAPINFOHEADER with twice the height, bottom-up rows) followed by an all-clear AND
# mask. The resource compiler (rc.exe), which builds these into SicExplorerCommand.dll, takes PNG frames only at 256 px and
# rejects smaller ones as "old DIB"; the DIB form is what every reader takes. The bitmap is disposed here.
function Dib([System.Drawing.Bitmap]$bitmap) {
    try {
        $size = $bitmap.Width
        $rowBytes = $size * 4
        $maskRowBytes = [int][Math]::Ceiling($size / 32.0) * 4
        $stream = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($stream)
        $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size * 2)); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]0); $writer.Write([uint32]($rowBytes * $size + $maskRowBytes * $size)); $writer.Write([int32]0); $writer.Write([int32]0); $writer.Write([uint32]0); $writer.Write([uint32]0)
        $data = $bitmap.LockBits([System.Drawing.Rectangle]::new(0, 0, $size, $size), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $row = [byte[]]::new($rowBytes)
            for ($y = $size - 1; $y -ge 0; $y--) {
                [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $data.Stride), $row, 0, $rowBytes)
                $writer.Write($row)
            }
        }
        finally { $bitmap.UnlockBits($data) }
        $writer.Write([byte[]]::new($maskRowBytes * $size))
        $writer.Flush()
        $stream.ToArray()
    }
    finally { $bitmap.Dispose() }
}

$sizes = @(16, 20, 24, 32, 48)
foreach ($agent in $Agents.Keys) {
    $hue, $letter = $Agents[$agent]
    $frames = @{}
    foreach ($size in $sizes) { $frames[$size] = [byte[]](Dib (Tile $hue $letter $size)) }

    # ICO layout: 6-byte header, a 16-byte directory entry per frame, then the DIB payloads in the same order.
    $ico = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($ico)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    foreach ($size in $sizes) {
        $bytes = [byte[]]$frames[$size]
        $writer.Write([byte]$size); $writer.Write([byte]$size); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$bytes.Length); $writer.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($size in $sizes) { $writer.Write([byte[]]$frames[$size]) }
    $writer.Flush()
    [IO.File]::WriteAllBytes((Join-Path $IconsDir "$agent.ico"), $ico.ToArray())
    "Wrote $agent.ico ($($ico.Length) bytes; frames $($sizes -join ', '))"
}

# OpenCode's own logo (its published opencode-logo-light.svg: a #211E1E 240x300 frame round a 120x180 window
# whose lower half is #CFCECD), drawn as art for a plated tile: AgentIcons.Source gives it SIC's #F1F1F1 plate, and the tile
# insets art to 76 % of its size, so the logo stands 82 % of this image's height, five eighths of the tile, as it did before
# October 12, 2026. Drawn on a 0.7 grid at 256 px, so its edges fall on whole pixels.
$size = 256; $scale = 0.7
$bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $left = [int](($size - 240 * $scale) / 2); $top = [int](($size - 300 * $scale) / 2)
    function Box([double]$x, [double]$y, [double]$w, [double]$h) { [System.Drawing.RectangleF]::new($left + $x * $scale, $top + $y * $scale, $w * $scale, $h * $scale) }
    $frame = [System.Drawing.Drawing2D.GraphicsPath]::new([System.Drawing.Drawing2D.FillMode]::Alternate)
    $frame.AddRectangle((Box 0 0 240 300)); $frame.AddRectangle((Box 60 60 120 180))
    $dark = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#211E1E'))
    $light = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#CFCECD'))
    $graphics.FillPath($dark, $frame)
    $graphics.FillRectangle($light, (Box 60 120 120 120))
    $dark.Dispose(); $light.Dispose(); $frame.Dispose()
}
finally { $graphics.Dispose() }
$bitmap.Save((Join-Path $IconsDir 'opencode.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
"Wrote opencode.png ($size px, OpenCode's logo)"
