#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes self-contained Emergency Archive binaries for Windows and Linux
  (spec sections 4 and 28, Phase 5) into the deployment layout.

.DESCRIPTION
  Produces:
    <out>\windows\EmergencyArchive.UI\   self-contained win-x64 single-file UI
    <out>\windows\VaultCli\              self-contained win-x64 VaultCli
    <out>\linux\EmergencyArchive.UI\     self-contained linux-x64 single-file UI
    <out>\linux\VaultCli\                self-contained linux-x64 VaultCli
    <out>\START-LINUX                    launcher script for the Linux UI

  Deploy to a prepared drive (scripts/new-usb.ps1) by copying:
    windows\EmergencyArchive.UI\*.exe  ->  drive root as START-WINDOWS.exe
    linux\EmergencyArchive.UI\*        ->  drive app\linux-x64\
    START-LINUX                        ->  drive root

.PARAMETER Configuration
  Build configuration. Default: Release.

.PARAMETER OutputDirectory
  Output root. Default: artifacts\publish.

.PARAMETER SkipLinux
  Skip the linux-x64 publishes.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts/publish',
    [switch]$SkipLinux
)

$ErrorActionPreference = 'Stop'
$repo = (Split-Path -Parent $PSScriptRoot)
Set-Location $repo

# Resolve an SDK-capable dotnet (same logic as setup-env.ps1): the first
# dotnet on PATH may be a runtime-only host after a per-user SDK install.
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

function Invoke-Publish([string]$Project, [string]$Rid, [string]$OutputPath)
{
    Write-Host "Publishing $Project ($Rid) -> $OutputPath" -ForegroundColor Cyan
    & $dotnet publish $Project -c $Configuration -r $Rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -o $OutputPath --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Project ($Rid)." }
}

$outRoot = Join-Path $repo $OutputDirectory

Invoke-Publish 'src/EmergencyArchive.UI/EmergencyArchive.UI.csproj' 'win-x64' (Join-Path $outRoot 'windows/EmergencyArchive.UI')
Invoke-Publish 'tools/VaultCli/VaultCli.csproj' 'win-x64' (Join-Path $outRoot 'windows/VaultCli')

if (-not $SkipLinux)
{
    Invoke-Publish 'src/EmergencyArchive.UI/EmergencyArchive.UI.csproj' 'linux-x64' (Join-Path $outRoot 'linux/EmergencyArchive.UI')
    Invoke-Publish 'tools/VaultCli/VaultCli.csproj' 'linux-x64' (Join-Path $outRoot 'linux/VaultCli')

    # START-LINUX launcher: makes the app binary executable on first run
    # (exFAT/FAT32 drives do not preserve the execute permission).
    $launcher = Join-Path $outRoot 'START-LINUX'
    $launcherLines = @(
        '#!/usr/bin/env bash',
        '# Emergency Archive launcher (Linux, spec section 4).',
        'set -e',
        'DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"',
        'chmod +x "$DIR/linux/EmergencyArchive.UI/EmergencyArchive.UI" 2>/dev/null || true',
        'exec "$DIR/linux/EmergencyArchive.UI/EmergencyArchive.UI"'
    )
    [IO.File]::WriteAllText($launcher, ($launcherLines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Wrote $launcher"
}

Write-Host ''
Write-Host 'Publish complete. Deployment hints (spec section 4):' -ForegroundColor Green
Write-Host '  windows: copy windows\EmergencyArchive.UI\EmergencyArchive.UI.exe to the drive root as START-WINDOWS.exe'
Write-Host '  linux:   copy START-LINUX + linux\EmergencyArchive.UI\* to the drive (chmod via START-LINUX on first run)'
