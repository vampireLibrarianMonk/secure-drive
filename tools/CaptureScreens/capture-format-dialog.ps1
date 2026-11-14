# Captures the real Windows "Format" dialog for the USB drive (with the File
# system dropdown showing exFAT) into reference_images/format-usb-exfat.png.
# The dialog is opened by open-format-dialog.ps1 in a child process and
# is closed with WM_CLOSE afterwards - nothing is ever formatted. Captures ONLY
# the dialog window rectangle, never the whole desktop.
param(
    [string]$OutPath = "reference_images/format-usb-exfat.png",
    [string]$DriveLetter = "D"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W32 {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[W32]::SetProcessDPIAware() | Out-Null
$drive = Get-PSDrive -Name $DriveLetter -ErrorAction SilentlyContinue
if (-not $drive) { Write-Output "SKIP: no $DriveLetter`: drive"; exit 0 }
$driveNumber = [uint32]([int][char]$DriveLetter - [int][char]'A')

function Find-FormatDialog
{
    $found = [IntPtr]::Zero
    $cb = [W32+EnumProc]{
        param($h, $l)
        if ([W32]::IsWindowVisible($h)) {
            $class = New-Object System.Text.StringBuilder 256
            [W32]::GetClassName($h, $class, 256) | Out-Null
            $title = New-Object System.Text.StringBuilder 256
            [W32]::GetWindowText($h, $title, 256) | Out-Null
            if ($class.ToString() -eq '#32770' -and $title.ToString() -like 'Format*') {
                $script:found = $h
                return $false
            }
        }
        return $true
    }
    [W32]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $found
}

# Open the dialog in a child process (it blocks there until closed). The child
# console is minimized but NOT hidden - with a hidden console the dialog did
# not appear in testing; minimized works reliably.
$child = Join-Path $PSScriptRoot 'open-format-dialog.ps1'
$proc = Start-Process powershell -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $child, '-DriveNumber', $driveNumber
) -WindowStyle Minimized -PassThru

# Wait for the dialog window to appear.
$hwnd = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    $hwnd = Find-FormatDialog
    if ($hwnd -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 400
}

if ($hwnd -eq [IntPtr]::Zero) {
    Write-Output 'SKIP: Format dialog did not appear'
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    exit 0
}

Start-Sleep -Milliseconds 900
[W32]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 500

$rect = New-Object W32+RECT
[W32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    Write-Output 'SKIP: empty dialog rect'
    [W32]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    exit 0
}

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$graphics.Dispose()
$bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output ("captured {0} ({1:N0} bytes, {2}x{3})" -f $OutPath, (Get-Item $OutPath).Length, $width, $height)

# Close the dialog - nothing was formatted.
[W32]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
if (-not $proc.WaitForExit(10000)) { Stop-Process -Id $proc.Id -Force }
Write-Output 'dialog closed'
