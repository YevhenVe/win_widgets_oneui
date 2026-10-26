# Dev helper: captures the primary screen (physical pixels) to a PNG, optionally a sub-rectangle.
param([string]$Out = "$PSScriptRoot\..\.screens\shot.png", [int[]]$Rect)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type 'using System.Runtime.InteropServices; public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr v); }'
[Dpi]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($Rect) { $b = New-Object System.Drawing.Rectangle $Rect[0], $Rect[1], $Rect[2], $Rect[3] }
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"$($b.Width)x$($b.Height)"
