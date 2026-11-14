# Polls for the SmartScreen "Windows protected your PC" dialog (title match,
# or any window owned by smartscreen.exe), captures its rectangle when it
# appears, then dismisses it with WM_CLOSE so the blocked app never runs.
# Designed to be run repeatedly in short cycles; exits 2 when not found yet.
param(
    [string]$OutPath = 'reference_images/windows-smartscreen-run-anyway.png',
    [int]$Seconds = 20
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W32P {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[W32P]::SetProcessDPIAware() | Out-Null
$deadline = (Get-Date).AddSeconds($Seconds)

while ((Get-Date) -lt $deadline) {
    $hwnd = [IntPtr]::Zero

    # Match 1: exact dialog title.
    $cb = [W32P+EnumProc]{
        param($h, $l)
        if ([W32P]::IsWindowVisible($h)) {
            $title = New-Object System.Text.StringBuilder 512
            [W32P]::GetWindowText($h, $title, 512) | Out-Null
            if ($title.ToString() -like '*Windows protected your PC*') {
                $script:hwnd = $h
                return $false
            }
        }
        return $true
    }
    [W32P]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null

    # Match 2 (fallback): visible window owned by smartscreen.exe, if it is
    # dialog-sized (avoids latching onto tiny hidden service windows).
    if ($hwnd -eq [IntPtr]::Zero) {
        $ssPids = @(Get-Process -Name smartscreen -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        foreach ($p in $ssPids) {
            $cb2 = [W32P+EnumProc]{
                param($h, $l)
                if ([W32P]::IsWindowVisible($h)) {
                    $pid2 = [uint32]0
                    [W32P]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
                    if ($pid2 -eq $p) {
                        $script:hwnd = $h
                        return $false
                    }
                }
                return $true
            }
            [W32P]::EnumWindows($cb2, [IntPtr]::Zero) | Out-Null
            if ($hwnd -ne [IntPtr]::Zero) { break }
        }
        if ($hwnd -ne [IntPtr]::Zero) {
            $r = New-Object W32P+RECT
            [W32P]::GetWindowRect($hwnd, [ref]$r) | Out-Null
            if (($r.Right - $r.Left) -lt 300 -or ($r.Bottom - $r.Top) -lt 200) { $hwnd = [IntPtr]::Zero }
        }
    }

    if ($hwnd -ne [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 900
        $rect = New-Object W32P+RECT
        [W32P]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        Write-Output ("dialog rect: {0}x{1} at ({2},{3})" -f $width, $height, $rect.Left, $rect.Top)
        if ($width -le 0 -or $height -le 0) { Write-Output 'SKIP: empty rect'; exit 1 }

        $bitmap = New-Object System.Drawing.Bitmap($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $graphics.Dispose()
        $bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        Write-Output ("captured {0} ({1:N0} bytes)" -f $OutPath, (Get-Item $OutPath).Length)

        # Dismiss = "Don't run": the app is never executed.
        [W32P]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Seconds 2
        Get-Process -Name 'START-WINDOWS' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $env:TEMP 'ea-smartscreen-probe') -Recurse -Force -ErrorAction SilentlyContinue
        Write-Output 'cleanup done'
        exit 0
    }

    Start-Sleep -Milliseconds 500
}
Write-Output 'not-found-yet'
exit 2
