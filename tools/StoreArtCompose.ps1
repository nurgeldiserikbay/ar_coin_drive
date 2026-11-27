# Builds the Google Play listing images in build/store-art from the raw 1920x1080 shots that the
# UI capture build takes with -storeart (build/uicapture/storeart).
#   1. Unity -executeMethod ARCoinDriveRelease.BuildUiCapture -buildTarget Win64
#   2. build/uicapture/ARCoinDrive.exe -storeart        (full screen for about a minute)
#   3. powershell -ExecutionPolicy Bypass -File tools/StoreArtCompose.ps1
Add-Type -AssemblyName System.Drawing
$proj = Split-Path -Parent $PSScriptRoot
$raw  = "$proj\build\uicapture\storeart"
$out  = "$proj\build\store-art"
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem $out -File | Remove-Item

$fonts = New-Object Drawing.Text.PrivateFontCollection
$fonts.AddFontFile("$proj\Assets\Resources\Fonts\ChakraPetch-Bold.ttf")
$fonts.AddFontFile("$proj\Assets\Resources\Fonts\ChakraPetch-SemiBold.ttf")
$bold = $fonts.Families | ? { $_.Name -eq 'Chakra Petch' } | Select-Object -First 1
if (-not $bold) { $bold = $fonts.Families[0] }

$jpeg = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | ? MimeType -eq 'image/jpeg'
$quality = New-Object Drawing.Imaging.EncoderParameters 1
$quality.Param[0] = New-Object Drawing.Imaging.EncoderParameter ([Drawing.Imaging.Encoder]::Quality), 93L

function New-Canvas($w, $h) {
    $b = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($b)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    return $b, $g
}

function Draw-Text($g, $text, $size, $color, $centerX, $y, $maxWidth, $style) {
    # One line: shrink until it fits, then a soft shadow and a dark outline under the fill.
    $probe = New-Object Drawing.Font $bold, $size, ([Drawing.FontStyle]$style), ([Drawing.GraphicsUnit]::Pixel)
    $measured = $g.MeasureString($text, $probe, 100000).Width
    if ($measured -gt $maxWidth * .97) { $size = $size * $maxWidth * .97 / $measured }
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $format = New-Object Drawing.StringFormat; $format.Alignment = 'Center'
    $rect = New-Object Drawing.RectangleF ($centerX - $maxWidth / 2 - 200), $y, ($maxWidth + 400), ($size * 3)
    $path.AddString($text, $bold, [int]$style, $size, $rect, $format)
    $shadow = $path.Clone(); $m = New-Object Drawing.Drawing2D.Matrix; $m.Translate(0, $size * .07); $shadow.Transform($m)
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(140, 0, 0, 0))), $shadow)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(200, 12, 16, 30)), ($size * .10); $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $path)
    $g.FillPath((New-Object Drawing.SolidBrush $color), $path)
}

function Fill-Background($g, $w, $h) {
    # The game's dark navy with an orange glow, like the menu.
    $rect = New-Object Drawing.Rectangle 0, 0, $w, $h
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, ([Drawing.Color]::FromArgb(255, 14, 20, 38)), ([Drawing.Color]::FromArgb(255, 32, 22, 52)), 90
    $g.FillRectangle($brush, $rect)
    $glow = New-Object Drawing.Drawing2D.GraphicsPath
    $glow.AddEllipse(-$w * .2, -$h * .55, $w * 1.4, $h * .9)
    $pg = New-Object Drawing.Drawing2D.PathGradientBrush $glow
    $pg.CenterColor = [Drawing.Color]::FromArgb(120, 240, 120, 20)
    $pg.SurroundColors = @([Drawing.Color]::FromArgb(0, 240, 120, 20))
    $g.FillPath($pg, $glow)
}

function Draw-Framed($g, $img, $x, $y, $w, $h) {
    $r = 26
    $frame = New-Object Drawing.Drawing2D.GraphicsPath
    $frame.AddArc($x, $y, $r * 2, $r * 2, 180, 90); $frame.AddArc($x + $w - $r * 2, $y, $r * 2, $r * 2, 270, 90)
    $frame.AddArc($x + $w - $r * 2, $y + $h - $r * 2, $r * 2, $r * 2, 0, 90); $frame.AddArc($x, $y + $h - $r * 2, $r * 2, $r * 2, 90, 90)
    $frame.CloseFigure()
    $shadow = $frame.Clone(); $m = New-Object Drawing.Drawing2D.Matrix; $m.Translate(0, 12); $shadow.Transform($m)
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(120, 0, 0, 0))), $shadow)
    $g.SetClip($frame)
    $g.DrawImage($img, $x, $y, $w, $h)
    $g.ResetClip()
    $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 245, 150, 30)), 6), $frame)
}

$white  = [Drawing.Color]::White
$orange = [Drawing.Color]::FromArgb(255, 255, 176, 46)
$shots = @(
    @('loop-clean',     'LOOP THE LOOP',             'Race tiny RC cars through loops and over jumps'),
    @('race-chase',     'RACE 3 RIVALS',             '5 laps, coins for boosts, every place counts'),
    @('jump',           'JUMP, SPIN, LAND IT',       'Steer in the air - clean landings pay coins'),
    @('race-phone',     'YOUR ROOM IS THE TRACK',    'AR on ARCore phones, or play in virtual rooms'),
    @('battle-chase',   'BUMPER BATTLES',            'Ram your rivals out of a shrinking ring'),
    @('backyard-chase', '8 TRACKS, 3 PLACES',        'House, backyard and workshop'),
    @('workshop-phone', 'PLAY WITH FRIENDS',         'Up to 4 players on the same Wi-Fi'),
    @('garage',         'UPGRADE YOUR CARS',         'Tune speed and grip, paint and style them')
)
$n = 1
foreach ($s in $shots) {
    $src = [Drawing.Image]::FromFile("$raw\art-$($s[0]).png")
    $b, $g = New-Canvas 1920 1080
    Fill-Background $g 1920 1080
    Draw-Text $g $s[1] 92 $white 960 18 1760 0
    Draw-Text $g $s[2] 44 $orange 960 128 1700 0
    # 16:9 game frame under the captions.
    Draw-Framed $g $src 260 200 1400 788
    $b.Save(("$out\screenshot-{0}.jpg" -f $n), $jpeg, $quality)
    $g.Dispose(); $b.Dispose(); $src.Dispose(); $n++
}

# Feature graphic 1024x500: the loop shot, a dark fade on the left, the logo.
$src = [Drawing.Image]::FromFile("$raw\art-loop-clean.png"); $b, $g = New-Canvas 1024 500
# Crop 1920x938 (2.048:1) around the loop, a little right of centre.
$g.DrawImage($src, (New-Object Drawing.Rectangle 0, 0, 1024, 500), (New-Object Drawing.Rectangle 0, 90, 1920, 938), [Drawing.GraphicsUnit]::Pixel)
$rect = New-Object Drawing.Rectangle 0, 0, 560, 500
$brush = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, ([Drawing.Color]::FromArgb(235, 14, 20, 38)), ([Drawing.Color]::FromArgb(0, 14, 20, 38)), 0
$g.FillRectangle($brush, $rect)
$logo = [Drawing.Image]::FromFile("$proj\Assets\Resources\ARCoinDriveLogo.png")
$g.DrawImage($logo, 6, 18, 370, 370)
Draw-Text $g 'Loops, jumps and RC races' 32 $white 196 392 360 0
$b.Save("$out\feature-graphic-1024x500.jpg", $jpeg, $quality)
$g.Dispose(); $b.Dispose(); $src.Dispose(); $logo.Dispose()

# App icon 512x512, 32-bit PNG.
$src = [Drawing.Image]::FromFile("$proj\Assets\Branding\AppIcon.png")
$b = New-Object Drawing.Bitmap 512, 512, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($b); $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
$g.DrawImage($src, 0, 0, 512, 512); $b.Save("$out\icon-512.png", [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $b.Dispose(); $src.Dispose()

Get-ChildItem $out -File | % { "{0}  {1} KB" -f $_.Name, [math]::Round($_.Length / 1KB) }
