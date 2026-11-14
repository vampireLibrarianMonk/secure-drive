# Captures the real Windows SmartScreen "Windows protected your PC" dialog
# (the blue first-launch warning) into
# reference_images/windows-smartscreen-run-anyway.png.
#
# How it works: a COPY of the app's own launcher is flagged with the standard
# Mark-of-the-Web (Zone.Identifier, ZoneId=3) in a temp folder and started
# through the shell (explorer.exe), exactly like a double-click. That engages
# the real SmartScreen hook while keeping this script unblocked, so it can poll
# for the dialog. (Launch the exe directly with Start-Process and the check
# happens synchronously inside the CreateProcess call - the caller blocks and
# never reaches its own poll loop.) The dialog is screenshotted (window
# rectangle only - never the desktop) and dismissed with WM_CLOSE, so the app
# never executes. The original on the stick is never touched.
# NOTE: needs a network where the SmartScreen reputation service answers;
# if the verdict call hangs, Windows fails open after ~60-90 s and runs the
# app - this script then stops it in the finally block.
param(
    [string]$OutPath = "reference_images/windows-smartscreen-run-anyway.png",
    [string]$SourceExe = "D:\START-WINDOWS.exe"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W32S {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[W32S]::SetProcessDPIAware() | Out-Null
if (-not (Test-Path $SourceExe)) { Write-Output "SKIP: source exe not found: $SourceExe"; exit 0 }

# Stage a flagged copy in temp (the original stays untouched).
$dir = Join-Path $env:TEMP 'ea-smartscreen-probe'
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$exe = Join-Path $dir (Split-Path $SourceExe -Leaf)
Copy-Item $SourceExe $exe -Force
Set-Content -Path $exe -Stream Zone.Identifier -Value "[ZoneTransfer]`nZoneId=3`n"

function Find-SmartScreenWindow
{
    $found = [IntPtr]::Zero
    $cb = [W32S+EnumProc]{
        param($h, $l)
        if ([W32S]::IsWindowVisible($h)) {
            $title = New-Object System.Text.StringBuilder 256
            [W32S]::GetWindowText($h, $title, 256) | Out-Null
            if ($title.ToString() -like '*Windows protected your PC*') {
                $script:found = $h
                return $false
            }
        }
        return $true
    }
    [W32S]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $found
}

try
{
    # Start through the shell, like a double-click: this is what engages the
    # SmartScreen hook, and it returns immediately (the check runs in the
    # shell/smartscreen processes, not here).
    Start-Process explorer.exe -ArgumentList $exe | Out-Null

    # SmartScreen checks reputation online, then shows the dialog.
    $hwnd = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        # Match 1: a window titled "Windows protected your PC".
        $found = [IntPtr]::Zero
        $cb = [W32S+EnumProc]{
            param($h, $l)
            if ([W32S]::IsWindowVisible($h)) {
                $title = New-Object System.Text.StringBuilder 512
                [W32S]::GetWindowText($h, $title, 512) | Out-Null
                if ($title.ToString() -like '*Windows protected your PC*') {
                    $script:found = $h
                    return $false
                }
            }
            return $true
        }
        [W32S]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
        if ($found -ne [IntPtr]::Zero) { $hwnd = $found; Write-Output 'found by title'; break }

        # Match 2: any visible top-level window owned by smartscreen.exe.
        $ssPids = @(Get-Process -Name smartscreen -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        if ($ssPids.Count -gt 0) {
            $byPid = [IntPtr]::Zero
            $cb2 = [W32S+EnumProc]{
                param($h, $l)
                if ([W32S]::IsWindowVisible($h)) {
                    $pid2 = [uint32]0
                    [W32S]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
                    if ($ssPids -contains $pid2) {
                        $script:byPid = $h
                        return $false
                    }
                }
                return $true
            }
            [W32S]::EnumWindows($cb2, [IntPtr]::Zero) | Out-Null
            if ($byPid -ne [IntPtr]::Zero) {
                # Only latch onto dialog-sized windows; smartscreen.exe also
                # owns tiny hidden service windows we must not capture.
                $r0 = New-Object W32S+RECT
                [W32S]::GetWindowRect($byPid, [ref]$r0) | Out-Null
                if ((($r0.Right - $r0.Left) -ge 300) -and (($r0.Bottom - $r0.Top) -ge 200)) {
                    $hwnd = $byPid; Write-Output 'found by smartscreen pid'; break
                }
            }
        }

        Start-Sleep -Milliseconds 600
    }

    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Output 'SKIP: SmartScreen dialog did not appear'
        exit 0
    }

    Start-Sleep -Milliseconds 900
    [W32S]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 600

    $rect = New-Object W32S+RECT
    [W32S]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    Write-Output ("dialog rect: {0}x{1} at ({2},{3})" -f $width, $height, $rect.Left, $rect.Top)
    if ($width -le 0 -or $height -le 0) { Write-Output 'SKIP: empty dialog rect'; exit 0 }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
    $graphics.Dispose()
    $bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Output ("captured {0} ({1:N0} bytes, {2}x{3})" -f $OutPath, (Get-Item $OutPath).Length, $width, $height)
}
finally
{
    # Dismiss the dialog (cancels the launch; the app never runs).
    if ($hwnd -ne [IntPtr]::Zero) {
        [W32S]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Seconds 2
    }
    $started = Get-Process -Name 'START-WINDOWS' -ErrorAction SilentlyContinue
    if ($started) { $started | Stop-Process -Force; Write-Output 'stopped unexpected app process' }
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output 'cleaned up'
}
