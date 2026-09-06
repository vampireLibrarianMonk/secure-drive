#Requires -Version 5.1
<#
.SYNOPSIS
  Runs the full Emergency Archive test suite on real Linux via Docker
  (spec section 28, Phase 5: Linux support), plus a self-contained
  linux-x64 deployment smoke test.

.DESCRIPTION
  Stages the repository (bin/obj/.git excluded) into a temp folder, mounts it
  into an Ubuntu-based .NET SDK container, and executes:

    1. dotnet test  — the entire suite (vault crypto, sync engine, FTS5
       search) on real Linux, where path/encoding/case differences surface.
    2. dotnet publish of VaultCli as a self-contained linux-x64 single-file
       binary, executed INSIDE the container — which has no .NET runtime —
       proving the self-contained deployment works on a bare Linux system.

  The Windows tree is never modified: everything happens inside the
  container's own filesystem. Requires Docker Desktop in Linux container
  mode and the mcr.microsoft.com/dotnet/sdk:10.0 image (pulled automatically).

.PARAMETER Image
  The .NET SDK container image. Default: mcr.microsoft.com/dotnet/sdk:10.0.

.PARAMETER SkipPull
  Skip 'docker pull' (image already present locally).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\test-linux.ps1
#>
[CmdletBinding()]
param(
    [string]$Image = 'mcr.microsoft.com/dotnet/sdk:10.0',
    [switch]$SkipPull
)

$ErrorActionPreference = 'Stop'
$repo = (Split-Path -Parent $PSScriptRoot)

# --- Precondition: Docker must be running Linux containers ------------------
$serverOs = docker version --format '{{.Server.Os}}' 2>$null
if ($LASTEXITCODE -ne 0 -or $serverOs -ne 'linux')
{
    throw "Docker is not running Linux containers (server OS: '$serverOs'). Start Docker Desktop and switch to Linux containers."
}

if (-not $SkipPull)
{
    Write-Host "Pulling $Image ..."
    docker pull $Image
    if ($LASTEXITCODE -ne 0) { throw 'docker pull failed.' }
}

# --- Stage the source into a temp folder (bin/obj/.git excluded) ------------
$stage = Join-Path ([IO.Path]::GetTempPath()) ("ea-linux-src-" + [guid]::NewGuid().ToString('N'))
Write-Host "Staging source to $stage ..."
robocopy $repo $stage /E /XD bin obj .git .idea artifacts node_modules /XF *.log *.err /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }

try
{
    # --- 1. Full test suite on Linux ----------------------------------------
    Write-Host '=== LINUX TEST SUITE ===' -ForegroundColor Cyan
    docker run --rm -v "${stage}:/src" -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e HOME=/tmp -w /src $Image `
        bash -lc "dotnet test EmergencyArchive.slnx -c Release --verbosity minimal"
    if ($LASTEXITCODE -ne 0) { throw "Linux test suite FAILED (exit $LASTEXITCODE)." }

    # --- 2. Self-contained linux-x64 deployment smoke ------------------------
    Write-Host '=== SELF-CONTAINED LINUX-X64 SMOKE ===' -ForegroundColor Cyan
    docker run --rm -v "${stage}:/src" -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e HOME=/tmp -w /src $Image `
        bash -lc "dotnet publish tools/VaultCli -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o /app -v minimal && echo '--- running self-contained VaultCli (system has no .NET runtime):' && /app/VaultCli | grep -q Usage && echo SELF-CONTAINED-VAULTCLI-SMOKE-OK"
    if ($LASTEXITCODE -ne 0) { throw "Linux publish smoke FAILED (exit $LASTEXITCODE)." }

    Write-Host ''
    Write-Host 'LINUX VALIDATION COMPLETE: full test suite + self-contained VaultCli smoke PASSED.' -ForegroundColor Green
}
finally
{
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
