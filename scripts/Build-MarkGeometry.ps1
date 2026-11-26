[CmdletBinding()]
param(
    [string]$ChromeDir = (Join-Path $PSScriptRoot '..\src\Sic.App\Assets\brand\chrome'),
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\Sic.App\Controls\MarkGeometry.g.cs'),
    [string]$GlyphSvg = (Join-Path $PSScriptRoot '..\docs\brand\resume-all.svg')
)
<#
Writes Controls\MarkGeometry.g.cs: the SIC mark as filled outlines for the XAML chrome, and the Resume all glyph.

The mark comes from the chrome's drawings in Assets\brand\chrome, in two sets: the mark above 48 px (sic.svg for a dark
surface, sic-light.svg for a light one) and the frames from 16 to 48 px (sic-16.svg to sic-48.svg, sic-light-16.svg to
sic-light-48.svg), each on its own pixel grid, which the chrome draws 1:1 where the mark's size on screen matches one.
They are the .ico's mark (Assets\brand\sic*.svg) redrawn in what XAML can draw, filled paths with a solid colour or a
linear gradient, by scripts\Build-MarkDrawings.ps1, which has the construction. Each path becomes one layer,
in the order the SVG paints them, with its opacity; class="surface" marks the layers SicMark lays over the colour of the
surface behind the mark. Run after changing those drawings, or the glyph's construction below.

The Resume all glyph is the shipped mark reduced to one colour on a 24-unit grid (redrawn at 16 units for menus): a ">_"
inside a circle that runs counter-clockwise round it and climbs into the "_"'s end, with lines at the rail's weights.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$invariant = [Globalization.CultureInfo]::InvariantCulture
$tolerance = 0.01

function Parse([string]$data) { [System.Windows.Media.Geometry]::Parse($data) }

function Union($a, $b) {
    if ($null -eq $a) { return $b }
    [System.Windows.Media.Geometry]::Combine($a, $b, [System.Windows.Media.GeometryCombineMode]::Union, $null, $tolerance, [System.Windows.Media.ToleranceType]::Absolute)
}

# The outline a stroke paints. SVG's default cap is butt and default join miter.
function Widen($geometry, [double]$width, [string]$cap = 'butt', [string]$join = 'miter', [string]$endCap = $cap) {
    $pen = [System.Windows.Media.Pen]::new([System.Windows.Media.Brushes]::Black, $width)
    $caps = @{ butt = 'Flat'; round = 'Round'; square = 'Square' }
    $pen.StartLineCap = $caps[$cap]; $pen.EndLineCap = $caps[$endCap]
    $pen.LineJoin = @{ miter = 'Miter'; round = 'Round'; bevel = 'Bevel' }[$join]
    $pen.MiterLimit = 4
    # A widened outline overlaps itself at joins; a union with nothing resolves it to one clean outline.
    Union ([System.Windows.Media.Geometry]::Empty) ($geometry.GetWidenedPathGeometry($pen, $tolerance, [System.Windows.Media.ToleranceType]::Absolute))
}

function Format([double]$value, [int]$decimals) {
    $text = [Math]::Round($value, $decimals).ToString("0.$('#' * $decimals)", $invariant)
    if ($text -eq '-0') { '0' } else { $text }
}

# Path markup for a geometry, every point moved by $map. Combine returns lines and Béziers; arcs are handled for the glyph.
function Serialize($geometry, [scriptblock]$map, [int]$decimals) {
    $path = [System.Windows.Media.PathGeometry]::CreateFromGeometry($geometry)
    $out = [System.Text.StringBuilder]::new()
    if ($path.FillRule -eq 'Nonzero') { [void]$out.Append('F1 ') }
    function Point([System.Windows.Point]$p) { $q = & $map $p; "$(Format $q.X $decimals) $(Format $q.Y $decimals)" }
    foreach ($figure in $path.Figures) {
        [void]$out.Append("M$(Point $figure.StartPoint)")
        foreach ($segment in $figure.Segments) {
            switch ($segment.GetType().Name) {
                'LineSegment' { [void]$out.Append(" L$(Point $segment.Point)") }
                'PolyLineSegment' { [void]$out.Append(' L' + (($segment.Points | ForEach-Object { Point $_ }) -join ' ')) }
                'BezierSegment' { [void]$out.Append(" C$(Point $segment.Point1) $(Point $segment.Point2) $(Point $segment.Point3)") }
                'PolyBezierSegment' { [void]$out.Append(' C' + (($segment.Points | ForEach-Object { Point $_ }) -join ' ')) }
                'QuadraticBezierSegment' { [void]$out.Append(" Q$(Point $segment.Point1) $(Point $segment.Point2)") }
                'PolyQuadraticBezierSegment' { [void]$out.Append(' Q' + (($segment.Points | ForEach-Object { Point $_ }) -join ' ')) }
                default { throw "Unexpected $($segment.GetType().Name) in a combined geometry." }
            }
        }
        if ($figure.IsClosed) { [void]$out.Append(' Z') }
    }
    return $out.ToString()
}

function Hex([string]$color) { $color.TrimStart('#').ToUpperInvariant() }

# A colour with its opacity, as RRGGBB or RRGGBBAA (Theme.Parse).
function Argb([string]$color, [string]$opacity) {
    $hex = Hex $color
    if (-not $opacity -or [double]::Parse($opacity, $invariant) -ge 1) { return $hex }
    $hex + ('{0:X2}' -f [int][Math]::Round([double]::Parse($opacity, $invariant) * 255))
}

# One of the chrome's drawings, as layer entries: every path in paint order, its data passed through (absolute M, L, C and
# Z, which XAML's path markup reads as SVG does), with its solid colour or linear gradient, its opacity, and whether SicMark
# lays it over the surface's colour.
function Drawing([string]$file, [string]$indent) {
    $xml = [xml](Get-Content -LiteralPath (Join-Path $ChromeDir $file) -Raw)
    $svg = $xml.DocumentElement
    $byId = @{}; foreach ($node in $svg.SelectNodes('//*[@id]')) { $byId[$node.GetAttribute('id')] = $node }
    foreach ($shape in $svg.ChildNodes) {
        if ($shape -isnot [System.Xml.XmlElement] -or $shape.LocalName -eq 'defs') { continue }
        if ($shape.LocalName -ne 'path' -or $shape.HasAttribute('stroke') -or $shape.HasAttribute('transform')) { throw "$file`: the chrome's drawings are filled paths only." }
        $data = $(if ($shape.GetAttribute('fill-rule') -eq 'evenodd') { '' } else { 'F1 ' }) + $shape.GetAttribute('d')
        $surface = if ($shape.GetAttribute('class') -eq 'surface') { 'true' } else { 'false' }
        $fill = $shape.GetAttribute('fill')
        if ($fill -match '^url\(#(.+)\)$') {
            $gradient = $byId[$Matches[1]]
            if ($gradient.GetAttribute('gradientUnits') -ne 'userSpaceOnUse') { throw "$file`: gradient $($Matches[1]) is not in user space." }
            if ($shape.HasAttribute('fill-opacity')) { throw "$file`: a path with a gradient has an opacity of its own." }
            $stops = ($gradient.ChildNodes | Where-Object { $_ -is [System.Xml.XmlElement] } | ForEach-Object { "($(Format ([double]::Parse($_.GetAttribute('offset'), $invariant)) 4), `"$(Argb $_.GetAttribute('stop-color') $_.GetAttribute('stop-opacity'))`")" }) -join ', '
            $ends = ('x1', 'y1', 'x2', 'y2' | ForEach-Object { Format ([double]::Parse($gradient.GetAttribute($_), $invariant)) 3 }) -join ', '
            $indent + "new(`"$data`", null, new($ends, [$stops]), $surface),"
        }
        else { $indent + "new(`"$data`", `"$(Argb $fill $shape.GetAttribute('fill-opacity'))`", null, $surface)," }
    }
}

# --- The Resume all glyph -------------------------------------------------------------------------------------------------
# The shipped mark (since 0.9.4) in one colour, so the rail's Resume all matches the icon: a ">" and a "_" on its
# baseline inside a circle that starts just below three o'clock, runs counter-clockwise all the way round and climbs into
# the "_"'s right end, where a short barb hangs from it as the head. Drawn on a 24-unit grid for the rail's 24 px (larger
# than its 16 px Segoe glyphs, since it carries more detail), and redrawn at 16 units for menus. Its lines keep the weights
# chosen for the glyph on 2026-09-29, a little heavier than the Segoe glyphs' 1 px: 1.3 px for the circle, the "_" and the
# barb, 1.5 px for the ">"; they keep that width at either size, while the shapes scale with the grid. Option A of a
# three-weight comparison on 2026-10-04 (1.3/1.5, 1.5/1.8, 1.8/2.2).
$grid = 24
$loopWidth = 1.3; $chevronWidth = 1.5
function P([double]$x, [double]$y) { "$($x.ToString('R', $invariant)) $($y.ToString('R', $invariant))" }
function Glyph([double]$size) {
    $k = $size / $grid; $centre = $size / 2
    $r = 10 * $k                                            # the circle's centre line
    $cx = 10 * $k; $cy = 12 * $k; $ca = 4 * $k              # the ">": (cx, cy - ca) to (cx + ca, cy) to (cx, cy + ca)
    $ux = 15.5 * $k; $uy = 16 * $k                          # the "_", on the ">"'s baseline
    $vx = 22 * $k; $barbEnd = 20.5 * $k                     # the head's corner on the "_", and where its barb ends
    # The circle leaves along the tangent that runs to the corner, arriving close to 45 degrees.
    $dx = $vx - $centre; $dy = $uy - $centre
    $phi = [Math]::Atan2($dy, $dx) + [Math]::Acos($r / [Math]::Sqrt($dx * $dx + $dy * $dy))
    $start = 6 * [Math]::PI / 180                           # just below three o'clock, clear of the "_"
    $arrow = Parse "M$(P ($centre + $r * [Math]::Cos($start)) ($centre + $r * [Math]::Sin($start))) A$r $r 0 1 0 $(P ($centre + $r * [Math]::Cos($phi)) ($centre + $r * [Math]::Sin($phi))) L$(P $vx $uy) L$(P $vx $barbEnd)"
    $glyph = Widen $arrow $loopWidth 'butt' 'round'
    $glyph = Union $glyph (Widen (Parse "M$(P $ux $uy) L$(P ($vx + $loopWidth / 2) $uy)") $loopWidth 'butt')
    $glyph = Union $glyph (Widen (Parse "M$(P $cx ($cy - $ca)) L$(P ($cx + $ca) $cy) L$(P $cx ($cy + $ca))") $chevronWidth 'round' 'round')
    Serialize $glyph { param($p) $p } 3
}
$glyphData = Glyph $grid
$menuData = Glyph 16

# The frames of one set, sic-<n>.svg (for dark surfaces) or sic-light-<n>.svg (for light ones), as dictionary entries.
function FrameSet([string]$prefix) {
    $files = Get-ChildItem -LiteralPath $ChromeDir -Filter "$prefix-*.svg" | Where-Object { $_.BaseName -match "^$prefix-(\d+)$" } | Sort-Object { [int]($_.BaseName -replace "^$prefix-", '') }
    if (-not $files) { throw "No frames ($prefix-<n>.svg) in $ChromeDir." }
    foreach ($file in $files) { "        [$($file.BaseName -replace "^$prefix-", '')] =`r`n        [`r`n$((Drawing $file.Name '            ') -join "`r`n")`r`n        ]," }
}

$full = Drawing 'sic.svg' '        '
$fullLight = Drawing 'sic-light.svg' '        '
$frames = FrameSet 'sic'
$framesLight = FrameSet 'sic-light'
$frameFiles = Get-ChildItem -LiteralPath $ChromeDir -Filter 'sic-*.svg' | Where-Object { $_.BaseName -match '^sic-(\d+)$' } | Sort-Object { [int]($_.BaseName -replace '^sic-', '') }
$source = @"
// <auto-generated>
// Written by scripts/Build-MarkGeometry.ps1 from Assets/brand/chrome: sic.svg, sic-light.svg and the frames sic-16.svg to
// sic-48.svg and sic-light-16.svg to sic-light-48.svg. Do not edit; change the drawings (scripts/
// Build-MarkDrawings.ps1) or the glyph's construction in the script, and run it again.
// </auto-generated>
namespace Sic.App.Controls;

internal static partial class SicMark
{
    /// <summary>The mark above 48 px, for a dark surface (Assets/brand/chrome/sic.svg): its shadow, the arrow, then the white &gt;_.</summary>
    private static readonly Layer[] FullCut =
    [
$($full -join "`r`n")
    ];

    /// <summary>The mark above 48 px, for a light surface (Assets/brand/chrome/sic-light.svg): its shadow, the arrow, then the near-black &gt;_.</summary>
    private static readonly Layer[] FullCutLight =
    [
$($fullLight -join "`r`n")
    ];

    /// <summary>
    /// The .ico's frames from 16 to 48 px (Assets/brand/chrome/sic-&lt;n&gt;.svg), each drawn on its own pixel grid and given
    /// here in pixels: the chrome draws one 1:1 where the mark's size on screen comes to its size. For a dark surface.
    /// </summary>
    private static readonly Dictionary<int, Layer[]> Frames = new()
    {
$($frames -join "`r`n")
    };

    /// <summary>The same frames for a light surface (Assets/brand/chrome/sic-light-&lt;n&gt;.svg), as in sic-light.ico.</summary>
    private static readonly Dictionary<int, Layer[]> FramesLight = new()
    {
$($framesLight -join "`r`n")
    };
}

internal static partial class ResumeAllMark
{
    /// <summary>The shipped mark in one colour, on a $grid-unit grid: a "&gt;_" inside a circle running counter-clockwise into the "_"'s end.</summary>
    private const string PathData = "$glyphData";

    private const double Grid = $grid;

    /// <summary>The same glyph redrawn at 16 units, the size of a menu item's icon, with its lines at the rail's widths.</summary>
    private const string MenuPathData = "$menuData";
}
"@
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutFile), $source.Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.UTF8Encoding]::new($true))

$glyphMarkup = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 $grid $grid" width="$grid" height="$grid">
  <title>SIC Resume all glyph</title>
  <desc>The SIC mark reduced to one colour for the rail's Resume all button: a "&gt;_" inside a circle that runs counter-clockwise round it and climbs into the "_"'s end. Written by scripts/Build-MarkGeometry.ps1.</desc>
  <path d="$($glyphData -replace '^F1 ', '')" fill="currentColor"$(if ($glyphData.StartsWith('F1 ')) { ' fill-rule="nonzero"' } else { ' fill-rule="evenodd"' })/>
</svg>
"@
[IO.File]::WriteAllText([IO.Path]::GetFullPath($GlyphSvg), $glyphMarkup.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
"Wrote $([IO.Path]::GetFullPath($OutFile)) ($($full.Count) layers above 48 px, frames $(($frameFiles | ForEach-Object { $_.BaseName -replace '^sic-', '' }) -join ', ')) and $([IO.Path]::GetFullPath($GlyphSvg))"
