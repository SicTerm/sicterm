[CmdletBinding()]
param(
    [string]$BrandDir = (Join-Path $PSScriptRoot '..\src\Sic.App\Assets\brand')
)
<#
Draws the SicTerm mark, a >_ prompt inside a circular arrow that runs counter-clockwise round it and climbs into the _'s end,
in the two sets the app ships: a white >_ with a cyan arrow for dark surfaces, and a near-black >_ with a deeper arrow for
light ones. Writes them into Assets\brand; run Build-Icon.ps1 (twice, as its help shows) and Build-MarkGeometry.ps1 after.

Two drawings of the same mark, for the two things that draw it:
- For the .ico (sic-<n>.svg and sic.svg, rendered by Edge with Build-Icon.ps1): SVG masks for the loop's colour along the
  sweep and the comet's fade, a blur for the _'s shadow on the head, and the drop shadow as a filter. It sits on any
  taskbar, so the fade is true transparency.
- For the chrome (chrome\sic-<n>.svg and chrome\sic.svg, baked into XAML by Build-MarkGeometry.ps1): XAML has no masks,
  blurs or filters (and bitmaps in the chrome crashed the XAML runtime), so this one is filled outlines and linear gradients
  only. The loop is cut into arcs of 15 degrees, each with a gradient along its chord sampled from the colour ramp, the light
  from above at the loop's centre line, and the comet's fade as each stop's alpha. Arcs marked class="surface" are laid
  over the colour of the surface behind the mark when SicMark draws them, which makes them opaque: neighbours then overlap
  with no seam, which arcs with true transparency cannot do (where they meet, each edge's anti-aliasing lets the surface
  show through, and where they overlap their alphas compound). The chrome's surfaces are SicTerm's own, so their colour is
  known. The _'s shadow on the head is a vertical gradient over the loop below the _, from the blur's profile; the drop
  shadow is the silhouette grown in eight steps, each a little wider, offset and stacked to the blur's profile, and the
  comet's faint stretch, up to where it is half opaque, casts none.
In both, the frames from 16 to 48 px are drawn each on its own pixel grid, with the same colours, light and layering and no
drop shadow: at those sizes it would be under 2 px, and drawing the mark at 88% to make room for it would lose the fit to
the grid. The mark on the 256 grid is drawn at 88% on its shadow. Rerunning overwrites every drawing.

The arrow's colours are SicTerm Dark's (October 17, 2026): the design's teal turned 8 degrees toward blue, as the accent was.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$inv = [Globalization.CultureInfo]::InvariantCulture
$tolerance = 0.01                 # set for each drawing in ChromeMark
function F([double]$v, [int]$decimals = 3) { $t = [Math]::Round($v, $decimals).ToString("0.$('#' * $decimals)", $inv); if ($t -eq '-0') { '0' } else { $t } }
function Sweeps([double]$from, [double]$to, [double]$step) {
    $list = [Collections.Generic.List[double]]::new()
    for ($s = $from; $s -lt $to - 0.01; $s += $step) { $list.Add($s) }
    $list.Add($to); , $list.ToArray()
}

# --- Colour, mixed in OKLab ramps ------------------------------------------------------------------------------------------
function Lin([double]$v) { if ($v -le 0.04045) { $v / 12.92 } else { [Math]::Pow(($v + 0.055) / 1.055, 2.4) } }
function Gam([double]$v) { $v = [Math]::Min(1.0, [Math]::Max(0.0, $v)); if ($v -le 0.0031308) { 12.92 * $v } else { 1.055 * [Math]::Pow($v, 1 / 2.4) - 0.055 } }
function ToLab([string]$hex) {
    $n = [Convert]::ToInt32($hex.Substring(1), 16)
    $r = Lin ((($n -shr 16) -band 255) / 255); $g = Lin ((($n -shr 8) -band 255) / 255); $b = Lin (($n -band 255) / 255)
    $l = [Math]::Cbrt(0.4122214708 * $r + 0.5363325363 * $g + 0.0514459929 * $b)
    $m = [Math]::Cbrt(0.2119034982 * $r + 0.6806995451 * $g + 0.1073969566 * $b)
    $s = [Math]::Cbrt(0.0883024619 * $r + 0.2817188376 * $g + 0.6299787005 * $b)
    @((0.2104542553 * $l + 0.7936177850 * $m - 0.0040720468 * $s), (1.9779984951 * $l - 2.4285922050 * $m + 0.4505937099 * $s), (0.0259040371 * $l + 0.7827717662 * $m - 0.8086757660 * $s))
}
function FromLab([double[]]$lab) {
    $l = [Math]::Pow($lab[0] + 0.3963377774 * $lab[1] + 0.2158037573 * $lab[2], 3)
    $m = [Math]::Pow($lab[0] - 0.1055613458 * $lab[1] - 0.0638541728 * $lab[2], 3)
    $s = [Math]::Pow($lab[0] - 0.0894841775 * $lab[1] - 1.2914855480 * $lab[2], 3)
    $rgb = (4.0767416621 * $l - 3.3077115913 * $m + 0.2309699292 * $s), (-1.2684380046 * $l + 2.6097574011 * $m - 0.3413193965 * $s), (-0.0041960863 * $l - 0.7034186147 * $m + 1.7076147010 * $s)
    '#' + (($rgb | ForEach-Object { '{0:X2}' -f [int][Math]::Round((Gam $_) * 255) }) -join '')
}
function Ramp([object[]]$stops, [double]$t) {
    if ($t -le $stops[0][0]) { return $stops[0][1] }
    for ($i = 1; $i -lt $stops.Count; $i++) {
        if ($t -le $stops[$i][0]) {
            $a = ToLab $stops[$i - 1][1]; $b = ToLab $stops[$i][1]; $u = ($t - $stops[$i - 1][0]) / ($stops[$i][0] - $stops[$i - 1][0])
            return FromLab @(0..2 | ForEach-Object { $a[$_] + ($b[$_] - $a[$_]) * $u })
        }
    }
    $stops[-1][1]
}
# White at $alpha over a colour, in sRGB, as the browser composites the light from above.
function Lit([string]$hex, [double]$alpha) {
    $n = [Convert]::ToInt32($hex.Substring(1), 16)
    '#' + ((16, 8, 0 | ForEach-Object { '{0:X2}' -f [int][Math]::Round(((($n -shr $_) -band 255) * (1 - $alpha)) + 255 * $alpha) }) -join '')
}
# The standard normal distribution, for a Gaussian blur's profile (Abramowitz and Stegun 7.1.26).
function Phi([double]$x) {
    $z = [Math]::Abs($x) / [Math]::Sqrt(2); $t = 1 / (1 + 0.3275911 * $z)
    $erf = 1 - ((((1.061405429 * $t - 1.453152027) * $t + 1.421413741) * $t - 0.284496736) * $t + 0.254829592) * $t * [Math]::Exp(-$z * $z)
    if ($x -lt 0) { (1 - $erf) / 2 } else { (1 + $erf) / 2 }
}

# --- The treatment, on the 256 grid ----------------------------------------------------------------
$tones = [ordered]@{
    light = @{ suffix = ''; hi = '#363F41'; lo = '#0A0A0A'; shadow = 0.28
               rise = @(@(0, '#00394D'), @(0.2, '#137C9B'), @(0.6, '#0A7192'), @(1, '#005E7C')) }
    dark = @{ suffix = '-ondark'; hi = '#FFFFFF'; lo = '#D6E3E8'; shadow = 0.5
              rise = @(@(0, '#00475B'), @(0.2, '#0B81A0'), @(1, '#2FB6D4')) }
}
$fade = @(@(0, '#2E2E2E'), @(0.55, '#FFFFFF'), @(1, '#FFFFFF'))   # the comet's mask: black is clear, white solid
function Opacity([double]$t) { [Convert]::ToInt32((Ramp $fade $t).Substring(1, 2), 16) / 255 }
$sky = 0.28                                             # the light from above: white at this alpha at the top, gone 55% down
$underShadow = @{ alpha = 0.45; dy = 5; sd = 3 }       # the _'s shadow on the head
$drop = @{ dy = 5; sd = 5; scale = 0.88 }              # the drop shadow, and the mark's scale to make room for it

# --- The layout on each frame's pixel grid ------------------------------------------------------------
$tight = @{
    16 = @{ lw = 2; ca = 3; ct = 3; cx = 6.5; cy = 8; ux = 11; hs = 4; tail = 7 }
    20 = @{ lw = 2; ca = 3; ct = 4; cx = 8; cy = 11; ux = 13; hs = 4; tail = 8.5 }
    24 = @{ lw = 3; ca = 4; ct = 6; cx = 10; cy = 12; ux = 17; hs = 4; tail = 4.5 }
    28 = @{ lw = 3; ca = 5; ct = 7; cx = 12.5; cy = 14; ux = 20; hs = 5; tail = 8 }
    30 = @{ lw = 4; ca = 5; ct = 7; cx = 12.5; cy = 15; ux = 21; hs = 5; tail = 1.5 }
    32 = @{ lw = 4; ca = 6; ct = 8; cx = 14; cy = 16; ux = 23; hs = 6; tail = 7 }
    36 = @{ lw = 5; ca = 6; ct = 9; cx = 15.5; cy = 18; ux = 25; hs = 6; tail = 3 }
    40 = @{ lw = 5; ca = 7; ct = 10; cx = 17; cy = 20; ux = 28; hs = 7; tail = 4.5 }
    42 = @{ lw = 5; ca = 7; ct = 10; cx = 18; cy = 21; ux = 30; hs = 7; tail = 4 }
    48 = @{ lw = 6; ca = 8; ct = 12; cx = 20; cy = 24; ux = 34; hs = 8; tail = 4.5; uy = 33 }
}
foreach ($n in @($tight.Keys)) { $tight[$n].n = $n }
$tight[256] = @{ n = 256; exact = $true; tail = $tight[48].tail }
foreach ($key in 'lw', 'uy', 'ca', 'ct', 'cx', 'cy', 'ux', 'hs') { $tight[256][$key] = $tight[48][$key] * 256 / 48 }

function Layout([hashtable]$g) {
    $o = @{ c = $g.n / 2 }
    $o.rad = $o.c - $g.lw / 2
    $o.cw = $g.ct / [Math]::Sqrt(2)
    $o.uy = if ($g.uy) { $g.uy } else { $g.cy + $g.ca + $(if (-not $g.exact -and $g.lw % 2) { 0.5 } else { 0 }) }
    $o.uTop = $o.uy - $g.lw / 2; $o.uBot = $o.uy + $g.lw / 2
    $o.px = $g.n - $g.lw / 2; $o.py = $o.uy
    $s = $o.px + $o.py
    $d = [Math]::Abs(2 * $o.c - $s) / [Math]::Sqrt(2)
    if ($d -ge $o.rad) {
        $dx = $o.px - $o.c; $dy = $o.py - $o.c
        $phi = [Math]::Atan2($dy, $dx) + [Math]::Acos($o.rad / [Math]::Sqrt($dx * $dx + $dy * $dy))
        $o.kx = $o.c + $o.rad * [Math]::Cos($phi); $o.ky = $o.c + $o.rad * [Math]::Sin($phi)
    } else {
        $h = [Math]::Sqrt($o.rad * $o.rad - $d * $d) / [Math]::Sqrt(2)
        $o.kx = $s / 2 - $h; $o.ky = $s / 2 + $h
    }
    $a = $g.tail * [Math]::PI / 180
    $o.sx = $o.c + $o.rad * [Math]::Cos($a); $o.sy = $o.c + $o.rad * [Math]::Sin($a)
    # The sweep: from the tail, counter-clockwise round the circle to the knee, where the loop leaves it for the head.
    $o.knee = [Math]::Atan2($o.ky - $o.c, $o.kx - $o.c) * 180 / [Math]::PI
    $o.sweep = 360 - ($o.knee - $g.tail)
    $o.loopD = "M $(F $o.sx 4) $(F $o.sy 4) A $(F $o.rad 4) $(F $o.rad 4) 0 1 0 $(F $o.kx 4) $(F $o.ky 4) L $(F $o.px 4) $(F $o.py 4) L $(F $o.px 4) $(F ($o.uBot + $g.hs) 4)"
    $o.chevD = "M $(F $g.cx 4) $(F ($g.cy - $g.ca) 4) L $(F ($g.cx + $g.ca) 4) $(F $g.cy 4) L $(F $g.cx 4) $(F ($g.cy + $g.ca) 4)"
    $o.top = $g.cy - $g.ca - $o.cw / 2; $o.foot = $g.cy + $g.ca + $o.cw / 2
    $o
}
# A point $s degrees round the loop from its tail (counter-clockwise), at radius $r, as "x y".
function At([hashtable]$g, [hashtable]$o, [double]$s, [double]$r) { $a = ($g.tail - $s) * [Math]::PI / 180; "$(F ($o.c + $r * [Math]::Cos($a)) 4) $(F ($o.c + $r * [Math]::Sin($a)) 4)" }
function Stops([object[]]$stops) { ($stops | ForEach-Object { "<stop offset='$(F $_[0] 4)' stop-color='$($_[1])'$(if ($_.Count -gt 2 -and $_[2] -lt 1) { " stop-opacity='$(F $_[2] 3)'" })/>" }) -join '' }
function Linear([string]$id, [double]$x1, [double]$y1, [double]$x2, [double]$y2, [object[]]$stops) {
    "<linearGradient id='$id' gradientUnits='userSpaceOnUse' x1='$(F $x1)' y1='$(F $y1)' x2='$(F $x2)' y2='$(F $y2)'>$(Stops $stops)</linearGradient>"
}

# --- For the .ico: A with masks and filters -------------------------------------------------------------------------------
function IcoMark([hashtable]$g, [hashtable]$tone, [bool]$master) {
    $o = Layout $g; $n = $g.n; $unit = $n / 256
    $region = "x='$(F (-$n / 4))' y='$(F (-$n / 4))' width='$(F (1.5 * $n))' height='$(F (1.5 * $n))'"
    # The loop's colour (or the fade's grey) along the sweep: thin wedges from the centre, each overlapping the one
    # before; the head takes the last colour.
    function Conic([object[]]$stops, [double]$step) {
        # Without anti-aliasing: at small sizes each wedge's soft edge let the surface show through along the seams, in
        # streaks. The loop's own edge comes from its mask, which keeps its anti-aliasing.
        $sb = [Text.StringBuilder]::new("<g shape-rendering='crispEdges'>")
        for ($a = -6.0; $a -lt $o.sweep - 0.01; $a += $step) {
            $b = [Math]::Min($a + $step, $o.sweep)
            $t = [Math]::Min(1.0, [Math]::Max(0.0, (($a + $b) / 2) / $o.sweep))
            [void]$sb.Append("<polygon points='$(F $o.c) $(F $o.c) $(At $g $o ($a - 0.8) (2 * $n)) $(At $g $o $b (2 * $n))' fill='$(Ramp $stops $t)'/>")
        }
        $head = (($o.sweep - 0.8), ($o.sweep + 9), ($o.sweep + 18), ($o.sweep + 27), 354 | ForEach-Object { At $g $o $_ (2 * $n) }) -join ' '
        [void]$sb.Append("<polygon points='$(F $o.c) $(F $o.c) $head' fill='$($stops[-1][1])'/>")
        [void]$sb.Append('</g>'); $sb.ToString()
    }
    $step = 2
    $loop = "<path d='$($o.loopD)' fill='none' stroke='#FFFFFF' stroke-width='$(F $g.lw)' stroke-linejoin='round'/>"
    $underD = "M $(F $g.ux) $(F $o.uy) H $n"
    $defs = "<mask id='loop' maskUnits='userSpaceOnUse' $region>$loop</mask>" +
        "<mask id='fade' maskUnits='userSpaceOnUse' $region>$(Conic $fade ($step * 1.5))</mask>" +
        "<filter id='blur' filterUnits='userSpaceOnUse' $region><feGaussianBlur stdDeviation='$(F ($underShadow.sd * $unit))'/></filter>" +
        (Linear 'sky' 0 0 0 $n @(@(0, '#FFFFFF', $sky), @(0.55, '#FFFFFF', 0))) +
        (Linear 'under' 0 $o.uTop 0 $o.uBot @(@(0, $tone.hi), @(1, $tone.lo))) +
        (Linear 'chev' 0 $o.top 0 $o.foot @(@(0, $tone.hi), @(1, $tone.lo)))
    $mark = "<g mask='url(#fade)'><g mask='url(#loop)'>$(Conic $tone.rise $step)<rect width='$n' height='$n' fill='url(#sky)'/>" +
        "<path d='$underD' fill='none' stroke='#000000' stroke-width='$(F $g.lw)' opacity='$($underShadow.alpha)' transform='translate(0 $(F ($underShadow.dy * $unit)))' filter='url(#blur)'/></g></g>" +
        "<path d='$underD' fill='none' stroke='url(#under)' stroke-width='$(F $g.lw)'/>" +
        "<path d='$($o.chevD)' fill='none' stroke='url(#chev)' stroke-width='$(F $o.cw)' stroke-linecap='round' stroke-linejoin='round'/>"
    if ($master) {
        $defs += "<filter id='drop' filterUnits='userSpaceOnUse' $region><feDropShadow dx='0' dy='$($drop.dy)' stdDeviation='$($drop.sd)' flood-color='#000000' flood-opacity='$($tone.shadow)'/></filter>"
        $mark = "<g transform='translate($($o.c) $($o.c)) scale($($drop.scale)) translate(-$($o.c) -$($o.c))'><g filter='url(#drop)'>$mark</g></g>"
    }
    ("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 $n $n' width='$n' height='$n'><defs>$defs</defs>$mark</svg>") -replace "'", '"'
}

# --- For the chrome: filled outlines and linear gradients, with WPF's geometry engine -------------------------------------
function Parse([string]$data) { [System.Windows.Media.Geometry]::Parse($data) }
function Combine($a, $b, [string]$mode) { [System.Windows.Media.Geometry]::Combine($a, $b, [System.Windows.Media.GeometryCombineMode]$mode, $null, $tolerance, [System.Windows.Media.ToleranceType]::Absolute) }
function Union($a, $b) { if ($null -eq $a) { return $b }; Combine $a $b 'Union' }
function Intersect($a, $b) { Combine $a $b 'Intersect' }
function Widen($geometry, [double]$width, [string]$cap, [string]$join) {
    $pen = [System.Windows.Media.Pen]::new([System.Windows.Media.Brushes]::Black, $width)
    $pen.StartLineCap = @{ butt = 'Flat'; round = 'Round' }[$cap]; $pen.EndLineCap = $pen.StartLineCap
    $pen.LineJoin = @{ miter = 'Miter'; round = 'Round' }[$join]
    Union ([System.Windows.Media.Geometry]::Empty) ($geometry.GetWidenedPathGeometry($pen, $tolerance, [System.Windows.Media.ToleranceType]::Absolute))
}
function Moved($geometry, [double]$dy) {
    $copy = $geometry.Clone(); $copy.Transform = [System.Windows.Media.TranslateTransform]::new(0, $dy)
    Union ([System.Windows.Media.Geometry]::Empty) $copy
}
# SVG path data for a geometry, every point moved by $map; Combine leaves only lines and Béziers.
function PathData($geometry, [scriptblock]$map) {
    $path = [System.Windows.Media.PathGeometry]::CreateFromGeometry($geometry)
    $sb = [Text.StringBuilder]::new()
    function Point([System.Windows.Point]$p) { $q = & $map $p; "$(F $q.X 2) $(F $q.Y 2)" }
    foreach ($figure in $path.Figures) {
        [void]$sb.Append("M$(Point $figure.StartPoint)")
        foreach ($segment in $figure.Segments) {
            switch ($segment.GetType().Name) {
                'LineSegment' { [void]$sb.Append(" L$(Point $segment.Point)") }
                'PolyLineSegment' { [void]$sb.Append(' L' + (($segment.Points | ForEach-Object { Point $_ }) -join ' ')) }
                'BezierSegment' { [void]$sb.Append(" C$(Point $segment.Point1) $(Point $segment.Point2) $(Point $segment.Point3)") }
                'PolyBezierSegment' { [void]$sb.Append(' C' + (($segment.Points | ForEach-Object { Point $_ }) -join ' ')) }
                'QuadraticBezierSegment' { [void]$sb.Append(" Q$(Point $segment.Point1) $(Point $segment.Point2)") }
                'PolyQuadraticBezierSegment' { [void]$sb.Append(' Q' + (($segment.Points | ForEach-Object { Point $_ }) -join ' ')) }
                default { throw "Unexpected $($segment.GetType().Name) in a combined geometry." }
            }
        }
        if ($figure.IsClosed) { [void]$sb.Append(' Z') }
    }
    @{ d = $sb.ToString(); rule = $(if ($path.FillRule -eq 'Nonzero') { 'nonzero' } else { 'evenodd' }) }
}

function ChromeMark([hashtable]$g, [hashtable]$tone, [bool]$master) {
    $o = Layout $g; $n = $g.n; $c = $o.c; $rad = $o.rad; $unit = $n / 256
    $k = if ($master) { $drop.scale } else { 1.0 }
    $map = { param($p) [System.Windows.Point]::new($c + $k * ($p.X - $c), $c + $k * ($p.Y - $c)) }.GetNewClosure()
    # Curves come out of Combine as lines this close to the true curve: 0.06 units on the 256 grid, 0.01 px at the least.
    $script:tolerance = [Math]::Max(0.01, 0.06 * $unit)
    function Point([double]$s, [double]$r) { $a = ($g.tail - $s) * [Math]::PI / 180; [System.Windows.Point]::new($c + $r * [Math]::Cos($a), $c + $r * [Math]::Sin($a)) }
    # Everything from the centre out between $s0 and $s1 degrees round: what cuts the loop into its arcs.
    function Sector([double]$s0, [double]$s1) { Parse "M $(F $c 4) $(F $c 4) L $(((Sweeps $s0 $s1 4) | ForEach-Object { At $g $o $_ (3 * $n) }) -join ' L ') Z" }

    $loop = Widen (Parse $o.loopD) $g.lw 'butt' 'round'
    $under = Parse "M $(F $g.ux 4) $(F $o.uTop 4) L $n $(F $o.uTop 4) L $n $(F $o.uBot 4) L $(F $g.ux 4) $(F $o.uBot 4) Z"
    $chevron = Widen (Parse $o.chevD) $o.cw 'round' 'round'

    $defs = [Text.StringBuilder]::new(); $body = [Text.StringBuilder]::new(); $count = @{ gradients = 0 }
    function Gradient([System.Windows.Point]$from, [System.Windows.Point]$to, [object[]]$stops) {
        $id = "g$((++$count.gradients))"
        $a = & $map $from; $b = & $map $to
        [void]$defs.Append((Linear $id $a.X $a.Y $b.X $b.Y $stops))
        "url(#$id)"
    }
    function Shape($geometry, [string]$fill, [double]$opacity = 1, [string]$class = '') {
        $p = PathData $geometry $map
        [void]$body.Append("<path$(if ($class) { " class='$class'" }) d='$($p.d)' fill='$fill'$(if ($opacity -lt 1) { " fill-opacity='$(F $opacity 3)'" })$(if ($p.rule -eq 'evenodd') { " fill-rule='evenodd'" })/>")
    }

    # The drop shadow: the silhouette grown in steps, each layer's alpha chosen so the stack follows the blur's profile at
    # the middle of each step. Outermost first.
    if ($master) {
        $cut = 0.0; while ((Opacity ($cut / $o.sweep)) -lt 0.5) { $cut += 1 }
        $silhouette = Union (Union $chevron $under) (Intersect $loop (Sector $cut 354))
        $spreads = 0, 1.5, 3, 4.5, 6, 7.5, 9, 10.5
        $target = for ($i = 0; $i -lt $spreads.Count; $i++) {
            $mid = if ($i -eq 0) { -0.75 } else { ($spreads[$i - 1] + $spreads[$i]) / 2 }
            $tone.shadow * (Phi (-$mid / $drop.sd))
        }
        for ($i = $spreads.Count - 1; $i -ge 0; $i--) {
            $alpha = if ($i -eq $spreads.Count - 1) { $target[$i] } else { 1 - (1 - $target[$i]) / (1 - $target[$i + 1]) }
            $grown = if ($spreads[$i] -eq 0) { $silhouette } else { Union $silhouette (Widen $silhouette (2 * $spreads[$i]) 'round' 'round') }
            Shape (Moved $grown $drop.dy) '#000000' $alpha
        }
    }

    # The loop's arcs, each running 1.5 degrees under the next; the head is one solid piece over the last.
    $arcs = [int][Math]::Ceiling(($o.sweep + 6) / 15)
    $bounds = 0..$arcs | ForEach-Object { -6 + ($o.sweep + 6) * $_ / $arcs }
    for ($i = 0; $i -lt $arcs; $i++) {
        $start = $bounds[$i]; $end = $bounds[$i + 1] + 1.5
        $a = Point $start $rad; $b = Point $end $rad
        $axis = [System.Windows.Vector]::new($b.X - $a.X, $b.Y - $a.Y)
        $stops = foreach ($s in (Sweeps $start $end 3)) {
            $t = [Math]::Min(1.0, [Math]::Max(0.0, $s / $o.sweep))
            $p = Point $s $rad
            $light = $sky * [Math]::Max(0.0, 1 - $p.Y / (0.55 * $n))
            , @(((($p.X - $a.X) * $axis.X + ($p.Y - $a.Y) * $axis.Y) / $axis.LengthSquared), (Lit (Ramp $tone.rise $t) $light), (Opacity $t))
        }
        Shape (Intersect $loop (Sector $start $end)) (Gradient $a $b $stops) 1 'surface'
    }
    Shape (Intersect $loop (Sector ($o.sweep - 1.5) 354)) (Ramp $tone.rise 1) 1 'surface'

    # The _'s shadow on the loop below it: the _ moved down and blurred, so a vertical gradient by the blur's profile.
    $dy = $underShadow.dy * $unit; $sd = $underShadow.sd * $unit
    $reach = $o.uBot + $dy + 3.5 * $sd
    $band = Parse "M $(F ($g.ux - $g.lw) 4) $(F $o.uBot 4) L $(F ($n + 1) 4) $(F $o.uBot 4) L $(F ($n + 1) 4) $(F $reach 4) L $(F ($g.ux - $g.lw) 4) $(F $reach 4) Z"
    $shade = foreach ($y in (Sweeps $o.uBot $reach (($reach - $o.uBot) / 8))) {
        , @((($y - $o.uBot) / ($reach - $o.uBot)), '#000000', ($underShadow.alpha * ((Phi (($o.uBot + $dy - $y) / $sd)) - (Phi (($o.uTop + $dy - $y) / $sd)))))
    }
    Shape (Intersect $loop $band) (Gradient ([System.Windows.Point]::new(0, $o.uBot)) ([System.Windows.Point]::new(0, $reach)) $shade)

    # The >_, each shaded from its top to its foot.
    Shape $under (Gradient ([System.Windows.Point]::new(0, $o.uTop)) ([System.Windows.Point]::new(0, $o.uBot)) @(@(0, $tone.hi), @(1, $tone.lo)))
    Shape $chevron (Gradient ([System.Windows.Point]::new(0, $o.top)) ([System.Windows.Point]::new(0, $o.foot)) @(@(0, $tone.hi), @(1, $tone.lo)))

    ("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 $n $n' width='$n' height='$n'><defs>$defs</defs>$body</svg>") -replace "'", '"'
}

$brand = (Resolve-Path -LiteralPath $BrandDir).Path
$chrome = Join-Path $brand 'chrome'
New-Item -ItemType Directory -Force -Path $chrome | Out-Null
foreach ($tone in $tones.Values) {
    $set = if ($tone.suffix -eq '-ondark') { 'sic' } else { 'sic-light' }
    foreach ($n in 16, 20, 24, 28, 30, 32, 36, 40, 42, 48, 256) {
        $name = if ($n -eq 256) { "$set.svg" } else { "$set-$n.svg" }
        Set-Content -LiteralPath (Join-Path $brand $name) -Value (IcoMark $tight[$n] $tone ($n -eq 256)) -Encoding utf8
        Set-Content -LiteralPath (Join-Path $chrome $name) -Value (ChromeMark $tight[$n] $tone ($n -eq 256)) -Encoding utf8
    }
}
"Drew the mark into $brand"
