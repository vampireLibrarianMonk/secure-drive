# Opens the real Windows "Format" dialog for one drive and keeps it open while
# the parent capture script screenshots it. NEVER formats anything: the dialog
# only formats when a person clicks Start, which never happens here. The parent
# closes the dialog with WM_CLOSE afterwards.
param([uint32]$DriveNumber = 3)  # A=0, B=1, C=2, D=3 ...

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SHFBridge {
    [DllImport("shell32.dll")]
    public static extern int SHFormatDrive(IntPtr hwnd, uint drive, uint fmtId, uint options);
}
"@

# SHFMT_ID_DEFAULT = 0xFFFF; options 0 = default dialog state. Blocking call:
# returns only when the dialog is dismissed.
[SHFBridge]::SHFormatDrive([IntPtr]::Zero, $DriveNumber, 0xFFFF, 0) | Out-Null
