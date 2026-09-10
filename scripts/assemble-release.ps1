#Requires -Version 7.0
<#
.SYNOPSIS
  Assembles a ready-to-use Emergency Archive drive layout into a folder (for
  release packaging), so the resulting ZIP can be extracted straight onto a USB
  stick. Cross-platform (pwsh on Windows or Linux CI runners).

.DESCRIPTION
  Produces the spec section 4 on-drive layout in <StageDir>:

      START-WINDOWS.exe / START-LINUX   (launcher at the drive root)
      app\windows\ | app\linux-x64\     (the application binaries)
      app\resources\USER-GUIDE.md       (the user guide, deployed with the app)
      vault\                            (empty; the app creates the vault on first launch)
      public\RECOVERY-INSTRUCTIONS.txt  (password-free recovery notes)
      README.txt                        (plain-text overview)
      .emergency-archive-drive.json     (layout marker)

  This never creates a vault and never touches a physical drive — it only lays
  files out in a folder. The user extracts the ZIP onto their stick and runs the
  launcher; the app shows CREATE YOUR ARCHIVE on first launch.

.PARAMETER Platform
  'windows' or 'linux'.

.PARAMETER PublishDir
  Folder containing the published, self-contained app for this platform
  (e.g. the UI publish output). For Windows it must contain
  EmergencyArchive.UI.exe; for Linux, EmergencyArchive.UI.

.PARAMETER CliPublishDir
  Optional folder containing the published VaultCli for this platform
  (VaultCli.exe on Windows, VaultCli on Linux). Copied into app\<platform>\.

.PARAMETER StageDir
  Output folder to assemble the layout into (created if missing).

.EXAMPLE
  pwsh scripts/assemble-release.ps1 -Platform windows `
       -PublishDir artifacts/publish/windows/EmergencyArchive.UI `
       -CliPublishDir artifacts/publish/windows/VaultCli `
       -StageDir stage/windows
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('windows', 'linux')][string]$Platform,
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [string]$CliPublishDir,
    [Parameter(Mandatory = $true)][string]$StageDir
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function New-Dir([string]$Path) { New-Item -ItemType Directory -Force -Path $Path | Out-Null }

# --- Fresh stage -----------------------------------------------------------
if (Test-Path $StageDir) { Remove-Item -Recurse -Force $StageDir }
New-Dir $StageDir
foreach ($d in 'app/windows', 'app/linux-x64', 'app/resources', 'vault', 'public') {
    New-Dir (Join-Path $StageDir $d)
}

# --- Application binaries ---------------------------------------------------
if ($Platform -eq 'windows') {
    $uiExe = Join-Path $PublishDir 'EmergencyArchive.UI.exe'
    if (-not (Test-Path $uiExe)) { throw "Windows UI build not found: $uiExe" }
    # Launcher at the drive root AND under app\windows (spec section 4).
    Copy-Item $uiExe (Join-Path $StageDir 'START-WINDOWS.exe') -Force
    Copy-Item $uiExe (Join-Path $StageDir 'app/windows/START-WINDOWS.exe') -Force
    if ($CliPublishDir) {
        $cli = Join-Path $CliPublishDir 'VaultCli.exe'
        if (Test-Path $cli) { Copy-Item $cli (Join-Path $StageDir 'app/windows/VaultCli.exe') -Force }
    }
}
else {
    $uiBin = Join-Path $PublishDir 'EmergencyArchive.UI'
    if (-not (Test-Path $uiBin)) { throw "Linux UI build not found: $uiBin" }
    Copy-Item (Join-Path $PublishDir '*') (Join-Path $StageDir 'app/linux-x64') -Recurse -Force
    if ($CliPublishDir) {
        $cli = Join-Path $CliPublishDir 'VaultCli'
        if (Test-Path $cli) { Copy-Item $cli (Join-Path $StageDir 'app/linux-x64/VaultCli') -Force }
    }
    # START-LINUX launcher at the drive root (makes the binary executable first
    # run, since exFAT does not preserve the execute bit).
    $startLinux = @(
        '#!/usr/bin/env bash',
        '# Emergency Archive launcher (Linux).',
        'set -e',
        'DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"',
        'chmod +x "$DIR/app/linux-x64/EmergencyArchive.UI" 2>/dev/null || true',
        'exec "$DIR/app/linux-x64/EmergencyArchive.UI"'
    ) -join "`n"
    $startPath = Join-Path $StageDir 'START-LINUX'
    [IO.File]::WriteAllText($startPath, $startLinux + "`n", (New-Object System.Text.UTF8Encoding($false)))
}

# --- README.txt -------------------------------------------------------------
$readme = @'
EMERGENCY ARCHIVE
=================

This USB drive contains an encrypted emergency document archive.

WHAT IS ON THIS DRIVE
  app/        the Emergency Archive application
  vault/      the encrypted archive (created on first launch; do not modify)
  public/     recovery instructions readable without any password

HOW TO USE IT
  Windows: open this drive and run START-WINDOWS.exe
  Linux:   open a terminal here and run ./START-LINUX
  First launch asks you to CREATE YOUR ARCHIVE (choose a password).
  Later launches ask for that password, then give you a search box.

IMPORTANT RULES
  - Do not save, move, rename, or delete any file inside 'vault/'.
  - There is no password reset. Store your password safely and never keep it
    on or with this drive.
  - Opening a document may leave temporary traces on the host computer;
    prefer a computer you trust.
  - See public/RECOVERY-INSTRUCTIONS.txt if the application ever stops working.
'@
Set-Content -Path (Join-Path $StageDir 'README.txt') -Value $readme -Encoding ascii

# --- public/RECOVERY-INSTRUCTIONS.txt --------------------------------------
$recovery = @'
EMERGENCY ARCHIVE - RECOVERY INSTRUCTIONS
=========================================

PURPOSE
  This file explains how to recover the encrypted documents on this USB drive
  WITHOUT the Emergency Archive application, using only the archive password and
  maintained open-source software. It contains no secrets.

ENCRYPTION FORMAT
  The documents are stored in a Cryptomator vault (format 8, cipher combination
  SIV_GCM) - a publicly documented, open-source format:
    - Docs:   https://docs.cryptomator.org (Security section)
    - Source: https://github.com/cryptomator/cryptolib
    - KDF:    scrypt (parameters stored in vault/masterkey.cryptomator)
  Unlocking requires exactly one secret: the archive password. There is no
  other key on this drive.

RECOVERY PROCEDURE (RECOMMENDED)
  1. Install the free, open-source Cryptomator application (https://cryptomator.org)
     on any Windows, macOS, or Linux computer.
  2. Plug in this USB drive.
  3. In Cryptomator: Add Vault -> Open existing vault -> select
     vault/masterkey.cryptomator on this drive.
  4. Enter the archive password. The vault opens as a virtual drive.
  5. Copy out the documents you need, then lock the vault.

IMPORTANT
  - Do not modify anything inside 'vault/'.
  - If integrity problems occur, stop writing to the drive and use another copy
    if you have one.
'@
Set-Content -Path (Join-Path $StageDir 'public/RECOVERY-INSTRUCTIONS.txt') -Value $recovery -Encoding ascii

# --- User guide (deployed with the app) ------------------------------------
$guideSrc = Join-Path $repoRoot 'docs/USER-GUIDE.md'
if (Test-Path $guideSrc) {
    Copy-Item $guideSrc (Join-Path $StageDir 'app/resources/USER-GUIDE.md') -Force
}

# --- Layout marker ----------------------------------------------------------
$version = (Get-Content (Join-Path $repoRoot 'VERSION') -TotalCount 1).Trim()
$marker = [ordered]@{
    schema     = 'emergency-archive-usb'
    layoutVer  = 1
    appVersion = $version
    platform   = $Platform
    createdUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    status     = 'layout-scaffolded'
}
$marker | ConvertTo-Json | Set-Content -Path (Join-Path $StageDir '.emergency-archive-drive.json') -Encoding ascii

Write-Host "Assembled $Platform release layout (v$version) in $StageDir"
