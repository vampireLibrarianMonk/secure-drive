# Lists visible dialog-class windows whose title contains a keyword.
# Used by the capture scripts' author to debug window discovery.
param([string]$TitleLike = '*Format*')
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W32Debug {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@
$found = @()
$cb = [W32Debug+EnumProc]{
    param($h, $l)
    if ([W32Debug]::IsWindowVisible($h)) {
        $c = New-Object System.Text.StringBuilder 256
        [W32Debug]::GetClassName($h, $c, 256) | Out-Null
        $t = New-Object System.Text.StringBuilder 256
        [W32Debug]::GetWindowText($h, $t, 256) | Out-Null
        $ts = $t.ToString()
        $pid2 = [uint32]0
        [W32Debug]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
        if ($ts -like $TitleLike -or $c.ToString() -eq '#32770') {
            $script:found += ('hwnd=0x{0:X} pid={1} class={2} title=[{3}]' -f $h.ToInt64(), $pid2, $c.ToString(), $ts)
        }
    }
    return $true
}
[W32Debug]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
$found | Select-Object -First 15
