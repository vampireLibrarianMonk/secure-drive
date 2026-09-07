#Requires -Version 5.1
<#
.SYNOPSIS
  Restore, build, and test the Emergency Archive inside the pinned .NET SDK
  container — no .NET SDK needs to be installed on the host.

.DESCRIPTION
  A convenience wrapper for developers (or CI) who have Docker but not the
  .NET SDK on the host. It mounts the working tree into
  mcr.microsoft.com/dotnet/sdk:<version> (version read from global.json) and
  runs the requested action against EmergencyArchive.slnx.

  This is a BUILD-TIME tool only. It has nothing to do with the shipped drive:
  the published binaries are self-contained (scripts/publish.ps1), so the drive
  still runs on a plain Windows/Linux computer with no SDK and no Docker. This
  script just moves the *build* into a container instead of a host SDK.

  The build runs on your actual working tree (mounted read-write), so bin/obj
  artifacts land in the repo exactly as a native `dotnet build` would produce
  them. NuGet packages are kept inside the container to avoid root-owned files
  appearing under your profile.

  Notes:
    - The Windows self-contained .exe is best produced on Windows with
      scripts/publish.ps1. Use -Publish here only for the linux-x64 payload
      (or a cross-compiled win-x64 payload, which this script can also emit).
    - For the dedicated Linux validation drill (staged copy + bare-Ubuntu
      self-contained smoke test) use scripts/test-linux.ps1 instead.

.PARAMETER Action
  build (default) | test | restore. 'test' implies a build.

.PARAMETER Configuration
  Build configuration. Default: Debug.

.PARAMETER Publish
  Also publish self-contained payloads for the given runtime(s) into
  artifacts/publish-docker. Accepts one or more RIDs, e.g. -Publish linux-x64
  or -Publish linux-x64,win-x64.

.PARAMETER Image
  The .NET SDK container image name (without tag). The tag is derived from
  global.json. Default: mcr.microsoft.com/dotnet/sdk.

.PARAMETER SkipPull
  Skip 'docker pull' (image already present locally).

.EXAMPLE
  # Build the whole solution in a container:
  powershell -ExecutionPolicy Bypass -File scripts\build-in-docker.ps1

.EXAMPLE
  # Build and run the full test suite:
  powershell -ExecutionPolicy Bypass -File scripts\build-in-docker.ps1 -Action test

.EXAMPLE
  # Release build plus a self-contained linux-x64 publish:
  powershell -ExecutionPolicy Bypass -File scripts\build-in-docker.ps1 -Configuration Release -Publish linux-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'restore')]
    [string]$Action = 'build',

    [string]$Configuration = 'Debug',

    [string[]]$Publish,

    [string]$Image = 'mcr.microsoft.com/dotnet/sdk',

    [switch]$SkipPull
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# --- Derive the SDK tag from global.json (stay pinned) ---------------------
# global.json pins e.g. 10.0.400; the SDK image is tagged by feature band
# (10.0), which the container satisfies as 10.0.4xx. Fall back to 10.0.
$sdkVersion = (Get-Content (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
$tag = if ($sdkVersion -match '^(\d+)\.(\d+)') { "$($Matches[1]).$($Matches[2])" } else { '10.0' }
$imageRef = "${Image}:${tag}"

# --- Precondition: Docker must be running Linux containers -----------------
$serverOs = docker version --format '{{.Server.Os}}' 2>$null
if ($LASTEXITCODE -ne 0 -or $serverOs -ne 'linux') {
    throw "Docker is not running Linux containers (server OS: '$serverOs'). Start Docker Desktop and switch to Linux containers."
}

if (-not $SkipPull) {
    Write-Host "Pulling $imageRef ..." -ForegroundColor Cyan
    docker pull $imageRef
    if ($LASTEXITCODE -ne 0) { throw 'docker pull failed.' }
}

# --- Compose the in-container command --------------------------------------
$sln = 'EmergencyArchive.slnx'
$steps = switch ($Action) {
    'restore' { @("dotnet restore $sln") }
    'build'   { @("dotnet build $sln -c $Configuration") }
    'test'    { @("dotnet test $sln -c $Configuration") }
}

if ($Publish) {
    foreach ($rid in $Publish) {
        $steps += "dotnet publish src/EmergencyArchive.UI/EmergencyArchive.UI.csproj -c $Configuration -r $rid --self-contained true -p:PublishSingleFile=true -o artifacts/publish-docker/$rid/EmergencyArchive.UI"
        $steps += "dotnet publish tools/VaultCli/VaultCli.csproj -c $Configuration -r $rid --self-contained true -p:PublishSingleFile=true -o artifacts/publish-docker/$rid/VaultCli"
    }
}

$script = ($steps -join ' && ')

Write-Host "=== $($Action.ToUpper()) in $imageRef ($Configuration) ===" -ForegroundColor Cyan
Write-Host "  $script"

# NUGET_PACKAGES + HOME inside the container keep restore artifacts off the
# host profile. The repo is mounted read-write so bin/obj land in the tree.
docker run --rm `
    -v "${repo}:/src" -w /src `
    -e NUGET_PACKAGES=/tmp/nuget `
    -e HOME=/tmp `
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 `
    -e DOTNET_NOLOGO=1 `
    $imageRef bash -lc $script
$exit = $LASTEXITCODE

Write-Host ''
if ($exit -eq 0) {
    Write-Host "$Action succeeded in $imageRef." -ForegroundColor Green
    if ($Publish) {
        Write-Host "Published payloads: artifacts\publish-docker\<rid>\" -ForegroundColor Green
        Write-Host 'Reminder: ship the Windows .exe from scripts\publish.ps1 (native Windows build) for release.'
    }
} else {
    throw "$Action FAILED in $imageRef (exit $exit)."
}
