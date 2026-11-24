# Draws the PhoneShare icon (blue tile, white phone, arrow going in) and saves a multi-size .ico.
param([Parameter(Mandatory = $true)][string]$OutFile)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$s) {
    $k = $s / 256.0
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $tile = New-RoundRect (4 * $k) (4 * $k) (248 * $k) (248 * $k) (60 * $k)
    $top = [System.Drawing.Color]::FromArgb(255, 79, 141, 255)
    $bottom = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new($s, $s)), $top, $bottom
    $g.FillPath($grad, $tile)

    $pen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::White), ([single][math]::Max(1.5, 15 * $k))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawPath($pen, (New-RoundRect (80 * $k) (36 * $k) (96 * $k) (184 * $k) (22 * $k)))
    $g.DrawLine($pen, [single](128 * $k), [single](80 * $k), [single](128 * $k), [single](158 * $k))
    $g.DrawLine($pen, [single](102 * $k), [single](132 * $k), [single](128 * $k), [single](158 * $k))
    $g.DrawLine($pen, [single](154 * $k), [single](132 * $k), [single](128 * $k), [single](158 * $k))
    $g.Dispose()
    return $bmp
}

# Classic 32-bit BMP icon entry (used for the small sizes).
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $maskStride = [int]([math]::Ceiling($s / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($s * $s * 4 + $maskStride * $s))
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $buf = New-Object byte[] ($data.Stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    $bmp.UnlockBits($data)
    for ($y = $s - 1; $y -ge 0; $y--) { $w.Write($buf, [int]($y * $data.Stride), [int]($s * 4)) }
    $w.Write((New-Object byte[] ($maskStride * $s)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) {
        $png = New-Object System.IO.MemoryStream
        $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $png.ToArray()
    } else {
        $bytes = Get-DibBytes $bmp
    }
    $bmp.Dispose()
    $images += , $bytes
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
