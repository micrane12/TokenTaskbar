$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
function Fill-Rounded($graphics,$color,[single]$x,[single]$y,[single]$width,[single]$height,[single]$radius) {
    $path=New-Object Drawing.Drawing2D.GraphicsPath
    $brush=New-Object Drawing.SolidBrush $color
    try {
        $d=$radius*2
        $path.AddArc($x,$y,$d,$d,180,90)
        $path.AddArc(($x+$width-$d),$y,$d,$d,270,90)
        $path.AddArc(($x+$width-$d),($y+$height-$d),$d,$d,0,90)
        $path.AddArc($x,($y+$height-$d),$d,$d,90,90)
        $path.CloseFigure()
        $graphics.FillPath($brush,$path)
    } finally {$brush.Dispose();$path.Dispose()}
}
$assets=Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
$sizes=@(16,20,24,32,48,64,128,256)
$frames=@()
foreach($size in $sizes) {
    $bitmap=New-Object Drawing.Bitmap $size,$size
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($size/32.0),($size/32.0))
        Fill-Rounded $graphics ([Drawing.Color]::FromArgb(28,28,34)) 1 1 30 30 7
        Fill-Rounded $graphics ([Drawing.Color]::FromArgb(216,135,99)) 5 7 22 6 3
        Fill-Rounded $graphics ([Drawing.Color]::FromArgb(130,148,232)) 5 19 22 6 3
        if($size -eq 256){$bitmap.Save((Join-Path $assets 'TokenTaskbar.png'),[Drawing.Imaging.ImageFormat]::Png)}
        $stream=New-Object IO.MemoryStream
        try {
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $frames+=,@($stream.ToArray())
        } finally {$stream.Dispose()}
    } finally {$graphics.Dispose();$bitmap.Dispose()}
}
$file=[IO.File]::Create((Join-Path $assets 'TokenTaskbar.ico'))
$writer=New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset=6+16*$frames.Count
    for($i=0;$i -lt $frames.Count;$i++) {
        $frame=$frames[$i]
        $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
        $writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Length)
        $writer.Write([uint32]$offset)
        $offset+=$frame.Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame)}
} finally {$writer.Dispose();$file.Dispose()}
