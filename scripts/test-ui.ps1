#Requires -Version 5.1
<#
.SYNOPSIS
  Run the headless Avalonia UI tests (screen-visibility invariants) in the
  pinned .NET SDK container — no host SDK and no display required.

.DESCRIPTION
  The UI tests render the real MainWindow in Avalonia's in-memory headless
  platform and assert that exactly one screen is visible in each application
  state (create / password / browse / setup). They guard against screen-layering
  regressions such as a misdirected IsVisible binding leaving two screens
  stacked.

  This is a fast inner-loop wrapper; the same tests also run as part of
  scripts\build-in-docker.ps1 -Action test and in CI (they are in the solution).

.PARAMETER Image
  The .NET SDK container image name (without tag). The tag is derived from
  global.json. Default: mcr.microsoft.com/dotnet/sdk.

.PARAMETER SkipPull
  Skip 'docker pull' (image already present locally).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\test-ui.ps1 -SkipPull
#>
[CmdletBinding()]
param(
    [string]$Image = 'mcr.microsoft.com/dotnet/sdk',
    [switch]$SkipPull
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$sdkVersion = (Get-Content (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
$tag = if ($sdkVersion -match '^(\d+)\.(\d+)') { "$($Matches[1]).$($Matches[2])" } else { '10.0' }
$imageRef = "${Image}:${tag}"

$serverOs = docker version --format '{{.Server.Os}}' 2>$null
if ($LASTEXITCODE -ne 0 -or $serverOs -ne 'linux') {
    throw "Docker is not running Linux containers (server OS: '$serverOs'). Start Docker Desktop and switch to Linux containers."
}

if (-not $SkipPull) {
    docker pull $imageRef
    if ($LASTEXITCODE -ne 0) { throw 'docker pull failed.' }
}

$proj = 'tests/EmergencyArchive.UI.Tests/EmergencyArchive.UI.Tests.csproj'

Write-Host "=== Headless UI tests in $imageRef ===" -ForegroundColor Cyan
docker run --rm `
    -v "${repo}:/src" -w /src `
    -e NUGET_PACKAGES=/tmp/nuget `
    -e HOME=/tmp `
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 `
    -e DOTNET_NOLOGO=1 `
    $imageRef bash -lc "dotnet test $proj -c Debug"
$exit = $LASTEXITCODE

Write-Host ''
if ($exit -eq 0) {
    Write-Host 'UI tests passed.' -ForegroundColor Green
} else {
    throw "UI tests FAILED (exit $exit)."
}
