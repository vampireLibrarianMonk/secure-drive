# Captures the Windows File Explorer window showing the USB drive root
# (reference_images/drive-contents-after-copy.png). Captures ONLY the Explorer
# window rectangle - never the full desktop. Fails gracefully (writes nothing)
# if the drive is not present or the window cannot be brought to front.
param(
    [string]$OutPath = "reference_images/drive-contents-after-copy.png"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[Win32]::SetProcessDPIAware() | Out-Null

$drive = Get-PSDrive -Name D -ErrorAction SilentlyContinue
if (-not $drive) { Write-Output 'SKIP: no D: drive'; exit 0 }

# Open File Explorer at the drive root.
Start-Process explorer.exe -ArgumentList 'D:\'
Start-Sleep -Seconds 4

$shell = New-Object -ComObject WScript.Shell
$activated = $shell.AppActivate('EMERGENCY')
if (-not $activated) { Write-Output 'SKIP: could not focus the EMERGENCY Explorer window'; exit 0 }
Start-Sleep -Milliseconds 800

$hwnd = [Win32]::GetForegroundWindow()
$title = New-Object System.Text.StringBuilder 256
[Win32]::GetWindowText($hwnd, $title, 256) | Out-Null
if ($title.ToString() -notmatch 'EMERGENCY') {
    Write-Output ("SKIP: foreground window is '{0}', not the drive window" -f $title)
    exit 0
}

$rect = New-Object Win32+RECT
[Win32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { Write-Output 'SKIP: empty window rect'; exit 0 }

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$graphics.Dispose()
$bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output ("captured {0} ({1:N0} bytes, window '{2}')" -f $OutPath, (Get-Item $OutPath).Length, $title)

# Leave the desktop as found: close the Explorer window (safe - a view only).
[Win32]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
