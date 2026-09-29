# Regenerates the Windows icon from the pulse mark in TrayIconFactory.cs.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$frames = foreach ($size in 16, 24, 32, 48, 64, 128, 256) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(28, 32, 40))
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 2.8 * $size / 32)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scale = $size / 32
    try {
        $graphics.FillEllipse($background, 0, 0, $size - 1, $size - 1)
        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(4 * $scale, 17 * $scale),
            [System.Drawing.PointF]::new(10 * $scale, 17 * $scale),
            [System.Drawing.PointF]::new(13 * $scale, 11 * $scale),
            [System.Drawing.PointF]::new(17 * $scale, 23 * $scale),
            [System.Drawing.PointF]::new(20 * $scale, 14 * $scale),
            [System.Drawing.PointF]::new(23 * $scale, 17 * $scale),
            [System.Drawing.PointF]::new(28 * $scale, 17 * $scale)
        )
        $graphics.DrawLines($pen, $points)
        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    }
    finally {
        $pen.Dispose()
        $background.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
        if ($stream) { $stream.Dispose() }
    }
}

$destination = Join-Path $PSScriptRoot 'AppIcon.ico'
$output = [System.IO.File]::Create($destination)
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $writer.Write([byte]($frame.Size % 256))
        $writer.Write([byte]($frame.Size % 256))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose() }

Get-Item -LiteralPath $destination | Select-Object FullName, Length
