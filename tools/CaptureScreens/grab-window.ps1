# Finds a visible top-level window by title pattern, brings it to the front,
# captures ONLY its rectangle to a PNG, and optionally closes it with WM_CLOSE.
# Used for genuine OS-dialog screenshots (Format dialog, SmartScreen, ...).
param(
    [Parameter(Mandatory = $true)][string]$TitleLike,
    [Parameter(Mandatory = $true)][string]$OutPath,
    [switch]$CloseAfter,
    [int]$SettleMs = 900
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W32G {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[W32G]::SetProcessDPIAware() | Out-Null

$found = [IntPtr]::Zero
$cb = [W32G+EnumProc]{
    param($h, $l)
    if ([W32G]::IsWindowVisible($h)) {
        $title = New-Object System.Text.StringBuilder 512
        [W32G]::GetWindowText($h, $title, 512) | Out-Null
        if ($title.ToString() -like $TitleLike) {
            $script:found = $h
            return $false
        }
    }
    return $true
}
[W32G]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null

if ($found -eq [IntPtr]::Zero) { Write-Output "SKIP: no window matching '$TitleLike'"; exit 0 }

[W32G]::SetForegroundWindow($found) | Out-Null
Start-Sleep -Milliseconds $SettleMs

$rect = New-Object W32G+RECT
[W32G]::GetWindowRect($found, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { Write-Output 'SKIP: empty window rect'; exit 0 }

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$graphics.Dispose()
$bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output ("captured {0} ({1:N0} bytes, {2}x{3})" -f $OutPath, (Get-Item $OutPath).Length, $width, $height)

if ($CloseAfter) {
    # WM_CLOSE cancels/dismisses; no button is ever clicked.
    [W32G]::PostMessage($found, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Write-Output 'window closed'
}
