# Writes Assets\NightMode.ico (16-256 px, PNG frames): white crescent on a graphite rounded square.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\Assets\NightMode.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$bg = [Drawing.Color]::FromArgb(255, 36, 36, 40)

$frames = foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'

    $r = [Math]::Max(3, $s * 0.22); $w = $s - 1
    $tile = New-Object Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $tile.AddArc($w - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $tile.AddArc($w - 2 * $r, $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $tile.AddArc(0, $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $tile.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush $bg), $tile)

    $p = $s * 0.2; $d = $s - 2 * $p
    $disc = New-Object Drawing.Drawing2D.GraphicsPath; $disc.AddEllipse($p, $p, $d, $d)
    $bite = New-Object Drawing.Drawing2D.GraphicsPath; $bite.AddEllipse($p + $d * 0.42, $p - $d * 0.2, $d, $d)
    $moon = New-Object Drawing.Region $disc; $moon.Exclude($bite)
    $g.FillRegion([Drawing.Brushes]::White, $moon)
    $g.Dispose()

    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($s, $ms.ToArray())
}

$fs = [IO.File]::Create($out)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $len = $f[1].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $bw.Write($f[1]) }
$bw.Close()
"Wrote $out"
