#Requires -Version 5.1
<#
.SYNOPSIS
  Prepares a USB drive with the Emergency Archive layout (spec section 4).

.DESCRIPTION
  Creates the standard on-drive layout:

      START-WINDOWS.exe   (added by the Phase 1 build; not created here)
      START-LINUX         (added by the Phase 5 build; not created here)
      README.txt          (created)
      app\windows\        (created)
      app\linux-x64\      (created)
      app\resources\      (created; USER-GUIDE.md copied here if present)
      vault\              (created; encrypted archive lives here - NEVER put plaintext here)
      public\             (created)
      public\RECOVERY-INSTRUCTIONS.txt  (created draft until Phase 0 decision)

  The script only ADDS files; it never deletes existing content. It refuses to
  touch a non-empty drive without -Force, and never touches the system drive.

.PARAMETER DriveLetter
  Drive letter of the target USB drive, e.g. D.

.PARAMETER Force
  Allow running against a drive that already contains files (nothing is deleted).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\new-usb.ps1 -DriveLetter D
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z]$')]
    [string]$DriveLetter,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$root = '{0}:\' -f $DriveLetter.ToUpperInvariant()

# --- Safety checks ---------------------------------------------------------
if (-not (Test-Path $root)) { throw "Drive $root was not found." }

$systemDrive = $env:SystemDrive.TrimEnd(':')
if ($DriveLetter.ToUpperInvariant() -eq $systemDrive) {
    throw "Refusing to operate on the system drive $root."
}

$markerPath = Join-Path $root '.emergency-archive-drive.json'
# OS-created folders that exist on any freshly formatted drive are not user data.
$osSystemEntries = @('System Volume Information', '$RECYCLE.BIN', 'RECYCLER', 'RECYCLED')
$existingItems = @(
    Get-ChildItem -Force -LiteralPath $root -ErrorAction SilentlyContinue |
        Where-Object { $osSystemEntries -notcontains $_.Name }
)
if ($existingItems.Count -gt 0 -and -not $Force -and -not (Test-Path $markerPath)) {
    throw (@"
Drive $root already contains $($existingItems.Count) item(s).
If you are certain it is safe to add files to it, re-run with -Force.
This script never deletes existing content.
"@)
}

$volume = Get-Volume -DriveLetter $DriveLetter.ToUpperInvariant() -ErrorAction SilentlyContinue
if ($volume) {
    Write-Host ("Target: {0} ({1:N1} GB free of {2:N1} GB, {3})" -f `
        $root, ($volume.SizeRemaining / 1GB), ($volume.Size / 1GB), $volume.FileSystemType)
}

# --- Folder layout (spec section 4) ----------------------------------------
Write-Host 'Creating folder layout...'
$folders = @(
    (Join-Path $root 'app\windows'),
    (Join-Path $root 'app\linux-x64'),
    (Join-Path $root 'app\resources'),
    (Join-Path $root 'vault'),
    (Join-Path $root 'public')
)
foreach ($folder in $folders) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    Write-Host "  + $folder"
}

# --- README.txt -------------------------------------------------------------
$readmePath = Join-Path $root 'README.txt'
$readme = @"
EMERGENCY ARCHIVE
=================

This USB drive contains an encrypted emergency document archive.

WHAT IS ON THIS DRIVE
  app\        the Emergency Archive application
  vault\      the encrypted archive (one file; do not modify or move it)
  public\     recovery instructions readable without any password

HOW TO USE IT
  1. Plug this drive into a Windows (or, later, Linux) computer.
  2. Run START-WINDOWS.exe.
  3. Enter the archive password.
  4. Search for the document you need, open or export it.
  5. Click LOCK (or close the application) when finished.

IMPORTANT RULES
  - Do not save, move, rename, or delete any file inside 'vault\'.
  - If the application reports an integrity problem, do not modify
    anything on this drive; see 'public\RECOVERY-INSTRUCTIONS.txt'.
  - Opening a document may leave temporary traces on the host computer.
    Prefer a computer you trust.
  - If you are not the archive owner, contact the person who gave you
    this drive for the password.

(Placeholder notice: START-WINDOWS.exe is produced by the Phase 1 build.
Until then this drive holds the prepared layout only.)
"@
Set-Content -Path $readmePath -Value $readme -Encoding ASCII
Write-Host "  + $readmePath"

# --- public\RECOVERY-INSTRUCTIONS.txt ---------------------------------------
$recoveryPath = Join-Path $root 'public\RECOVERY-INSTRUCTIONS.txt'
$recovery = @"
EMERGENCY ARCHIVE - RECOVERY INSTRUCTIONS
=========================================

PURPOSE
  This file explains how to recover the encrypted documents on this USB
  drive WITHOUT the Emergency Archive application, using only the archive
  password and maintained open-source software. It contains no secrets:
  never write the password or any key material into this file.

STATUS
  DRAFT. The final encrypted vault format is selected in Phase 0
  (docs\CRYPTOGRAPHY.md in the project repository). When the format is
  decided, the exact steps below will be completed and this file will be
  regenerated by the build. The final version will identify:

    1. The encryption format and format/version.
    2. The exact files on this drive containing encrypted data.
    3. Compatible open-source recovery software and where to obtain it.
    4. Step-by-step unlock/decrypt instructions for a technically
       competent person.

  What is already true and will not change:

    - The documents are encrypted with a format that can be opened by
      independently maintained open-source software, not only by the
      Emergency Archive application.
    - Unlocking requires the archive password (the one given to you by
      the archive owner). There is no other key on this drive.
    - 'vault\' is the only location holding encrypted document data.
    - Do not reformat, delete files in, or "clean up" this drive.

IF SOMETHING IS WRONG
  - The application says the archive is corrupt: stop writing to this
    drive, keep it safe, and check whether another replica exists.
  - This file is missing: contact the archive owner; a fresh copy can
    be exported from Setup Mode.
"@
Set-Content -Path $recoveryPath -Value $recovery -Encoding ASCII
Write-Host "  + $recoveryPath"

# --- app\resources\USER-GUIDE.md --------------------------------------------
$userGuideSource = Join-Path $repoRoot 'docs\USER-GUIDE.md'
if (Test-Path $userGuideSource) {
    Copy-Item $userGuideSource (Join-Path $root 'app\resources\USER-GUIDE.md') -Force
    Write-Host "  + $(Join-Path $root 'app\resources\USER-GUIDE.md')"
}

# --- Drive marker (used for replica identification and safe re-runs) --------
$marker = [ordered]@{
    schema     = 'emergency-archive-usb'
    layoutVer  = 1
    createdUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    archiveId  = $null
    archiveVer = $null
    status     = 'layout-scaffolded'
}
$marker | ConvertTo-Json | Set-Content -Path $markerPath -Encoding ASCII
Write-Host "  + $markerPath"

Write-Host ''
Write-Host "Drive $root prepared." -ForegroundColor Green
Write-Host 'Note: START-WINDOWS.exe / START-LINUX appear after the Phase 1 build (docs\BUILD.md).'
