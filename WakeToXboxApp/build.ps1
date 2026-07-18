$ErrorActionPreference = 'Stop'

$src = $PSScriptRoot
$outDir = Join-Path (Split-Path $src -Parent) 'dist'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory $outDir | Out-Null }
$out = Join-Path $outDir 'WakeToXboxApp.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$ico = Join-Path $outDir 'app.ico'

Add-Type -AssemblyName System.Drawing
$logoSource = Join-Path (Split-Path $src -Parent) 'assets\icon.png'
$cropFactor = 0.84

function New-LogoBitmap([int]$s, [System.Drawing.Image]$srcImg) {
    $cw = [int]($srcImg.Width * $script:cropFactor)
    $ch = [int]($srcImg.Height * $script:cropFactor)
    $cx = [int](($srcImg.Width - $cw) / 2)
    $cy = [int](($srcImg.Height - $ch) / 2)

    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
    $clip.AddEllipse(0, 0, $s, $s)
    $g.SetClip($clip)
    $dest = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $srcRect = New-Object System.Drawing.Rectangle($cx, $cy, $cw, $ch)
    $g.DrawImage($srcImg, $dest, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)

    $g.Dispose(); $clip.Dispose()
    return $bmp
}

function Get-BmpEntry([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]40); $bw.Write([int]$s); $bw.Write([int]($s * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]($s * $s * 4 + $maskRow * $s))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $data.Stride, $s * 4) }
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Close()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$srcImg = [System.Drawing.Image]::FromFile($logoSource)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-LogoBitmap $s $srcImg
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += ,$ms.ToArray()
    } else {
        $pngs += ,(Get-BmpEntry $bmp)
    }
    $bmp.Dispose()
}
$srcImg.Dispose()

$fs = [System.IO.File]::Create($ico)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $sizeByte = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$sizeByte); $bw.Write([byte]$sizeByte)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Close()
Write-Host "Generated $ico ($($sizes -join ', ') px)"

& $csc /nologo /target:winexe /optimize+ "/out:$out" "/win32icon:$ico" `
    /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $src '*.cs')

if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Write-Host "Built $out"
