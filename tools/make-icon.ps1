# Builds app.ico: indigo rounded-square background + white padlock.
# ASCII only - Windows PowerShell 5.1 reads BOM-less files as ANSI and mangles non-ASCII.
Add-Type -AssemblyName System.Drawing

$OutPath = $args[0]
if (-not $OutPath) { throw "Pass output path as first argument." }

$Sizes = @(16, 32, 48, 64, 128, 256)
$Indigo = [System.Drawing.Color]::FromArgb(255, 99, 102, 241)
$IndigoDark = [System.Drawing.Color]::FromArgb(255, 79, 70, 229)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    [float]$d = $r * 2
    $path.AddArc([float]$x, [float]$y, $d, $d, [float]180, [float]90)
    $path.AddArc([float]($x + $w - $d), [float]$y, $d, $d, [float]270, [float]90)
    $path.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, [float]0, [float]90)
    $path.AddArc([float]$x, [float]($y + $h - $d), $d, $d, [float]90, [float]90)
    $path.CloseFigure()
    return $path
}

$images = @()

foreach ($size in $Sizes) {
    [float]$s = $size
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Background: rounded square with vertical gradient.
    $bgPath = New-RoundedPath 0 0 $s $s ([float]($s * 0.22))
    $rect = New-Object System.Drawing.RectangleF([float]0, [float]0, $s, $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $Indigo, $IndigoDark, [float]90)
    $g.FillPath($brush, $bgPath)
    $brush.Dispose()
    $bgPath.Dispose()

    # Padlock shackle: half circle above the body. Drawn first so the body covers its ends.
    [float]$shackleW = $s * 0.30
    [float]$shackleH = $s * 0.30
    [float]$shackleX = ($s - $shackleW) / 2
    [float]$shackleY = $s * 0.22
    [float]$penW = [Math]::Max(1.5, $s * 0.09)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, $shackleX, $shackleY, $shackleW, [float]($shackleH * 1.6), [float]180, [float]180)
    $pen.Dispose()

    # Padlock body: rounded rectangle in the lower half.
    [float]$bodyW = $s * 0.50
    [float]$bodyH = $s * 0.38
    [float]$bodyX = ($s - $bodyW) / 2
    [float]$bodyY = $s * 0.45
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $bodyPath = New-RoundedPath $bodyX $bodyY $bodyW $bodyH ([float]($s * 0.08))
    $g.FillPath($white, $bodyPath)
    $bodyPath.Dispose()
    $white.Dispose()

    # Keyhole: punched out in the body. Skipped at 16px where it turns to mush.
    if ($size -ge 32) {
        [float]$holeD = $s * 0.14
        [float]$holeX = ($s - $holeD) / 2
        [float]$holeY = $bodyY + $bodyH * 0.26
        $holeBrush = New-Object System.Drawing.SolidBrush($IndigoDark)
        $g.FillEllipse($holeBrush, $holeX, $holeY, $holeD, $holeD)
        $holeBrush.Dispose()
    }

    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $images += , @{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
}

# ICO container: ICONDIR (6 bytes) + ICONDIRENTRY (16 bytes each) + PNG payloads.
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)

$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }   # 256 is encoded as 0
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$img.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write($img.Bytes) }

$bw.Flush()
[System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())
$bw.Close(); $out.Dispose()
"ok: $OutPath ($((Get-Item $OutPath).Length) bytes, sizes $($Sizes -join '/'))"
