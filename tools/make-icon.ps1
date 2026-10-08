# Generates src/PowerPen/Assets/PowerPen.ico: a blue rounded square with the pen symbol, at every size
# Windows asks for (taskbar, tray, Explorer, Alt+Tab). Run with Windows PowerShell:
#   powershell -STA -ExecutionPolicy Bypass -File tools\make-icon.ps1
# Needs the Segoe Fluent Icons font (built into Windows 11).

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$output = Join-Path $PSScriptRoot '..\src\PowerPen\Assets\PowerPen.ico'

function Render-Png([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    # Background: rounded square in the toolbar's "active" blue.
    $radius = $size * 0.22
    $background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x2F, 0x5F, 0x8F))
    $dc.DrawRoundedRectangle($background, $null, [System.Windows.Rect]::new(0, 0, $size, $size), $radius, $radius)

    # Foreground: the toolbar's pen glyph, white, centered.
    $typeface = New-Object System.Windows.Media.Typeface 'Segoe Fluent Icons'
    $text = New-Object System.Windows.Media.FormattedText ([string][char]0xEE56), ([Globalization.CultureInfo]::InvariantCulture),
        ([System.Windows.FlowDirection]::LeftToRight), $typeface, ($size * 0.66), ([System.Windows.Media.Brushes]::White), 1.0
    $dc.DrawText($text, [System.Windows.Point]::new(($size - $text.Width) / 2, ($size - $text.Height) / 2))
    $dc.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    return ,$stream.ToArray()
}

# ICO layout: 6-byte header, one 16-byte directory entry per size, then each image (PNG data is allowed since Vista).
$images = $sizes | ForEach-Object { ,(Render-Png $_) }
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dimension = if ($size -ge 256) { 0 } else { $size }   # 0 means 256 in the ICO format
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)         # no palette, reserved
    $writer.Write([uint16]1); $writer.Write([uint16]32)    # planes, bits per pixel
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()

New-Item -ItemType Directory -Force (Split-Path $output) | Out-Null
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $output)).Path + '\PowerPen.ico', $file.ToArray())
"Wrote $output ($($file.Length) bytes, sizes: $($sizes -join ', '))"

# Preview image of the 256 and 32 versions, for checking by eye.
[System.IO.File]::WriteAllBytes((Join-Path $env:TEMP 'PowerPen-icon-256.png'), $images[-1])
[System.IO.File]::WriteAllBytes((Join-Path $env:TEMP 'PowerPen-icon-32.png'), $images[3])
