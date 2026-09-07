#Requires -Version 5.1
<#
.SYNOPSIS
  First-use setup for an Emergency Archive drive (for example the D: drive):
  scaffold the layout, deploy the application, and create the encrypted vault
  so the drive is immediately usable.

.DESCRIPTION
  This is the one command to run the first time you prepare a drive. It ties
  together the existing, tested building blocks so you do not have to run them
  by hand or in the right order:

    1. Prepares the on-drive layout          (scripts\new-usb.ps1)
    2. Deploys the built application to it    (from artifacts\publish, if built)
    3. Creates the encrypted vault            (VaultCli create <drive>\vault)

  After this, plug the drive into any Windows computer, run START-WINDOWS.exe,
  enter the password, and (as the owner) open SETUP -> ESTATE PLANNING to seed
  the recommended folders and add your documents.

  The script only ADDS files; it never deletes existing content, refuses the
  system drive, and never writes the password anywhere. Creating the vault is
  skipped automatically if one already exists on the drive (safe to re-run).

.PARAMETER DriveLetter
  Drive letter of the target drive, e.g. D.

.PARAMETER PublishRoot
  Where the published binaries live. Default: artifacts\publish. If the Windows
  build is not present, the app-deploy step is skipped with a note (the layout
  and vault are still created).

.PARAMETER SkipVault
  Prepare the layout and deploy the app, but do not create the vault yet.

.PARAMETER SkipApp
  Prepare the layout and create the vault, but do not deploy the app binaries.

.PARAMETER Reset
  DESTRUCTIVE. Delete the existing vault\ and public\ (start completely fresh),
  then lay the drive out again. All archived documents are lost permanently and
  there is no password recovery. Requires a typed confirmation unless -Force.
  Only vault\ and public\ are removed; other files on the drive are left alone.

.PARAMETER Force
  Allow running against a drive that already contains files (nothing is
  deleted), and skip the interactive confirmation for -Reset.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\setup-drive.ps1 -DriveLetter D

.EXAMPLE
  # Wipe the archive and start fresh (prompts for confirmation):
  powershell -ExecutionPolicy Bypass -File scripts\setup-drive.ps1 -DriveLetter D -Reset
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z]$')]
    [string]$DriveLetter,

    [string]$PublishRoot = 'artifacts/publish',

    [switch]$SkipVault,
    [switch]$SkipApp,
    [switch]$Reset,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$letter = $DriveLetter.ToUpperInvariant()
$root = '{0}:\' -f $letter

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

# --- 0. Reset (start fresh) - DESTRUCTIVE ----------------------------------
# Removes the existing encrypted archive and public files so the drive can be
# set up from scratch. Everything in vault\ (all archived documents) is lost
# permanently and there is no password recovery, so we require an explicit,
# typed confirmation unless -Force is given. Only vault\ and public\ are
# touched; other files on the drive are left alone.
if ($Reset) {
    $systemDrive = $env:SystemDrive.TrimEnd(':')
    if ($letter -eq $systemDrive) { throw "Refusing to reset the system drive $root." }
    if (-not (Test-Path $root)) { throw "Drive $root was not found." }

    $vaultDir = Join-Path $root 'vault'
    $publicDir = Join-Path $root 'public'

    Write-Step "Reset requested for $root"
    Write-Host "This will PERMANENTLY delete the encrypted archive and public files:" -ForegroundColor Yellow
    Write-Host "    $vaultDir   (all archived documents - unrecoverable)"
    Write-Host "    $publicDir  (recovery / estate readme files)"
    Write-Host "Other files on $root are left untouched."

    if (-not $Force) {
        $answer = Read-Host "Type the drive letter '$letter' to confirm deletion (anything else cancels)"
        if ($answer -ne $letter) { throw 'Reset cancelled - nothing was deleted.' }
    }

    foreach ($dir in @($vaultDir, $publicDir)) {
        if (Test-Path $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force
            Write-Host "  removed $dir"
        }
    }
    Write-Host "Reset complete - the drive will be laid out fresh below." -ForegroundColor Green
}

# --- 1. Layout -------------------------------------------------------------
Write-Step "Preparing drive layout on $root"
$newUsb = Join-Path $PSScriptRoot 'new-usb.ps1'
$newUsbArgs = @{ DriveLetter = $letter }
if ($Force) { $newUsbArgs['Force'] = $true }
& $newUsb @newUsbArgs

# --- 2. Deploy the application (best effort) -------------------------------
if ($SkipApp) {
    Write-Step 'Skipping application deploy (-SkipApp)'
} else {
    Write-Step 'Deploying the application to the drive'
    $publish = if ([IO.Path]::IsPathRooted($PublishRoot)) { $PublishRoot } else { Join-Path $repo $PublishRoot }

    $winExe = Join-Path $publish 'windows\EmergencyArchive.UI\EmergencyArchive.UI.exe'
    $winCli = Join-Path $publish 'windows\VaultCli\VaultCli.exe'
    $linuxUi = Join-Path $publish 'linux\EmergencyArchive.UI'
    $startLinux = Join-Path $publish 'START-LINUX'

    if (Test-Path $winExe) {
        # The Windows launcher lives at the drive root as START-WINDOWS.exe
        # (spec section 4); VaultLocator walks up from app\windows to find vault\.
        Copy-Item $winExe (Join-Path $root 'START-WINDOWS.exe') -Force
        Copy-Item $winExe (Join-Path $root 'app\windows\START-WINDOWS.exe') -Force
        Write-Host "  + $(Join-Path $root 'START-WINDOWS.exe')"
        if (Test-Path $winCli) {
            Copy-Item $winCli (Join-Path $root 'app\windows\VaultCli.exe') -Force
            Write-Host "  + $(Join-Path $root 'app\windows\VaultCli.exe')"
        }
    } else {
        Write-Warning "Windows build not found at $winExe."
        Write-Host   '  Build it first:  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1'
        Write-Host   '  The layout and vault are still prepared; deploy the app later.'
    }

    if (Test-Path $linuxUi) {
        Copy-Item $linuxUi (Join-Path $root 'app\linux-x64') -Recurse -Force
        if (Test-Path $startLinux) { Copy-Item $startLinux (Join-Path $root 'START-LINUX') -Force }
        Write-Host "  + $(Join-Path $root 'app\linux-x64')"
    }
}

# --- 3. Create the encrypted vault -----------------------------------------
$vaultDir = Join-Path $root 'vault'
$masterkey = Join-Path $vaultDir 'masterkey.cryptomator'

if ($SkipVault) {
    Write-Step 'Skipping vault creation (-SkipVault)'
} elseif (Test-Path $masterkey) {
    Write-Step 'Vault already exists on this drive — leaving it untouched'
    Write-Host "  Found $masterkey"
} else {
    Write-Step 'Creating the encrypted vault'

    # Prefer the VaultCli we just deployed to the drive; otherwise fall back to
    # a locally built VaultCli or `dotnet run`. Vault creation must use the
    # real, tested crypto (VaultStore.Create) — never a reimplementation.
    $cli = @(
        (Join-Path $root 'app\windows\VaultCli.exe'),
        (Join-Path $repo 'artifacts\publish\windows\VaultCli\VaultCli.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    Write-Host 'You will be asked for the archive password twice.'
    Write-Host 'Choose a long passphrase (four or five random words). There is NO password reset.' -ForegroundColor Yellow

    if ($cli) {
        & $cli create $vaultDir
        if ($LASTEXITCODE -ne 0) { throw 'Vault creation failed.' }
    } else {
        Write-Warning 'No built VaultCli found; using `dotnet run` (requires the .NET SDK).'
        Push-Location $repo
        try {
            & dotnet run --project tools\VaultCli\VaultCli.csproj -- create $vaultDir
            if ($LASTEXITCODE -ne 0) { throw 'Vault creation failed.' }
        } finally {
            Pop-Location
        }
    }
    Write-Host "  Vault created at $vaultDir" -ForegroundColor Green
}

Write-Host ''
Write-Host "Drive $root is ready for first use." -ForegroundColor Green
Write-Host 'Next steps:'
Write-Host "  1. Open $root and run START-WINDOWS.exe (if the app was deployed)."
Write-Host '  2. Enter the archive password you just chose.'
Write-Host '  3. Click SETUP, then RUN ESTATE-PLANNING SETUP to seed folders and'
Write-Host '     write a letter for your family, then ADD your documents folder and UPDATE.'
Write-Host '  See docs\FIRST-USE.md for the full walkthrough.'
