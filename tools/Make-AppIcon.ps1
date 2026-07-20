<#
.SYNOPSIS
    Renders the Dispatch badge into a multi-resolution app.ico for the executable.

.DESCRIPTION
    The window/taskbar icon while the app runs is rendered from the ArtBadge
    geometry at startup, but the .exe file itself (File Explorer, pinned
    shortcuts, the desktop shortcut, the taskbar before the window shows) uses an
    icon embedded at build time via <ApplicationIcon>. This draws that icon from
    the same badge geometry so the mark is consistent everywhere, and writes a
    PNG-payload .ico with every size Windows asks for.
#>

Add-Type -AssemblyName System.Drawing

$navy = [System.Drawing.Color]::FromArgb(255, 17, 27, 46)    # #111B2E
$navyDeep = [System.Drawing.Color]::FromArgb(255, 11, 18, 32) # #0B1220
$gold = [System.Drawing.Color]::FromArgb(255, 232, 180, 74)   # #E8B44A

# The badge lives on a 120x140 field.
$outerPts = @(60,4, 74,18, 94,14, 100,34, 118,44, 108,62, 118,80, 100,90,
              94,110, 74,106, 60,120, 46,106, 26,110, 20,90, 2,80, 12,62,
              2,44, 20,34, 26,14, 46,18)

function New-BadgePaths {
    $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = for ($i = 0; $i -lt $outerPts.Length; $i += 2) {
        New-Object System.Drawing.PointF($outerPts[$i], $outerPts[$i + 1])
    }
    $outer.AddPolygon([System.Drawing.PointF[]]$pts)

    $shield = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shield.AddLine(60, 26, 96, 40)
    $shield.AddLine(96, 40, 96, 66)
    $shield.AddBezier(96, 66, 96, 86, 81, 100, 60, 108)
    $shield.AddBezier(60, 108, 39, 100, 24, 86, 24, 66)
    $shield.AddLine(24, 66, 24, 40)
    $shield.CloseFigure()

    return @{ Outer = $outer; Shield = $shield }
}

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render-Size([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Navy rounded tile with a subtle top-down gradient, and a faint gold rim.
    $margin = [single]($S * 0.06)
    $tile = New-RoundedRect $margin $margin ($S - 2 * $margin) ($S - 2 * $margin) ([single]($S * 0.20))
    $rect = New-Object System.Drawing.RectangleF(0, 0, $S, $S)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $navy, $navyDeep, 90)
    $g.FillPath($grad, $tile)
    $rimPen = New-Object System.Drawing.Pen($gold, [single]([Math]::Max(1, $S / 128.0)))
    $rimPen.Color = [System.Drawing.Color]::FromArgb(70, 232, 180, 74)
    $g.DrawPath($rimPen, $tile)

    # Map the 120x140 badge field into the tile, centred, ~64% of the icon.
    $scale = [single](($S * 0.62) / 140.0)
    $offX = [single](($S - 120 * $scale) / 2.0)
    $offY = [single](($S - 140 * $scale) / 2.0)
    $state = $g.Save()
    $g.TranslateTransform($offX, $offY)
    $g.ScaleTransform($scale, $scale)

    $paths = New-BadgePaths
    $goldBrush = New-Object System.Drawing.SolidBrush($gold)
    $navyBrush = New-Object System.Drawing.SolidBrush($navy)

    # Filled gold star, navy shield cut into it, thin gold rim on the shield —
    # reads as a badge at every size, including 16px.
    $g.FillPath($goldBrush, $paths.Outer)
    $g.FillPath($navyBrush, $paths.Shield)
    $shieldPen = New-Object System.Drawing.Pen($gold, [single]4)
    $shieldPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($shieldPen, $paths.Shield)

    $g.Restore($state)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) {
    $bytes = Render-Size $s
    $pngs.Add($bytes)
    Write-Host ("  {0}px -> {1:N0} bytes" -f $s, $bytes.Length)
}

# Assemble an .ico with PNG payloads (Vista+; every modern surface reads it).
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)  # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))   # width
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))   # height
    $bw.Write([Byte]0); $bw.Write([Byte]0)                    # colours, reserved
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)               # planes, bpp
    $bw.Write([UInt32]$data.Length)                           # bytes in resource
    $bw.Write([UInt32]$offset)                                # offset
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush()

$dest = Join-Path $PSScriptRoot "..\src\Dispatch.App\app.ico"
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($dest), $out.ToArray())
Write-Host ("Wrote {0} ({1} sizes, {2:N0} bytes)" -f $dest, $sizes.Count, $out.Length)
