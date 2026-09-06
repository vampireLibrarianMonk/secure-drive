#Requires -Version 5.1
<#
.SYNOPSIS
  Sets up and verifies the Emergency Archive development environment.

.DESCRIPTION
  1. Verifies a .NET SDK matching global.json is available.
  2. Restores the pinned local dotnet tools (.config/dotnet-tools.json, SBOM toolchain).
  3. Restores NuGet packages, builds the solution, and runs the tests.

  Safe to run repeatedly; every step is idempotent.

.PARAMETER SkipTests
  Skip the test run.

.PARAMETER Configuration
  Build configuration. Default: Debug.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\setup-env.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
# 1. .NET SDK
# ---------------------------------------------------------------------------
Write-Step 'Checking .NET SDK'

$globalJsonPath = Join-Path $repoRoot 'global.json'
$requiredSdk = (Get-Content $globalJsonPath -Raw | ConvertFrom-Json).sdk.version

function Test-RequiredSdkAvailable {
    $list = & dotnet --list-sdks 2>$null
    return ($null -ne $list -and ($list -match [regex]::Escape($requiredSdk)))
}

function Ensure-RequiredSdk {
    # The first dotnet on PATH may be a runtime-only host (no SDK), e.g. when a
    # per-user SDK install has not been picked up by the parent environment yet.
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        if (Test-RequiredSdkAvailable) { return }
    }

    $userDotnetDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    if (Test-Path (Join-Path $userDotnetDir 'dotnet.exe')) {
        $env:Path = "$userDotnetDir;$env:Path"
        if (Test-RequiredSdkAvailable) {
            Write-Host "Using user-local dotnet at $userDotnetDir"
            return
        }
    }

    throw (@"
A .NET SDK matching global.json ($requiredSdk) was not found.
Install it (see docs\BUILD.md):
  winget install Microsoft.DotNet.SDK.$($requiredSdk.Split('.')[0])
  or: https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.ps1 (-Channel $($requiredSdk.Split('.')[0]))
"@)
}

Ensure-RequiredSdk
$sdks = & dotnet --list-sdks
Write-Host "dotnet: $((Get-Command dotnet).Source)"
Write-Host "Installed SDKs: $($sdks -join ' | ')"
Write-Host "Required SDK $requiredSdk found." -ForegroundColor Green

# ---------------------------------------------------------------------------
# 2. Local dotnet tools (SBOM toolchain, pinned in the repo manifest)
# ---------------------------------------------------------------------------
Write-Step 'Restoring local dotnet tools'

& dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

# ---------------------------------------------------------------------------
# 3. NuGet restore
# ---------------------------------------------------------------------------
Write-Step 'Restoring NuGet packages'

& dotnet restore EmergencyArchive.slnx
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

# ---------------------------------------------------------------------------
# 4. Build
# ---------------------------------------------------------------------------
Write-Step "Building ($Configuration)"

& dotnet build EmergencyArchive.slnx --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

# ---------------------------------------------------------------------------
# 5. Tests
# ---------------------------------------------------------------------------
if ($SkipTests) {
    Write-Step 'Skipping tests (-SkipTests)'
} else {
    Write-Step 'Running tests'

    & dotnet test EmergencyArchive.slnx --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
}

Write-Host ""
Write-Host 'Environment ready. Next steps:' -ForegroundColor Green
Write-Host '  - Generate the SBOM:        powershell -ExecutionPolicy Bypass -File scripts\generate-sbom.ps1'
Write-Host '  - Prepare a USB drive:      powershell -ExecutionPolicy Bypass -File scripts\new-usb.ps1 -DriveLetter D'
Write-Host '  - Read the user guide:      docs\USER-GUIDE.md'
