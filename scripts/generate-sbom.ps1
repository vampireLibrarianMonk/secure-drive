#Requires -Version 5.1
<#
.SYNOPSIS
  Generates a CycloneDX SBOM for the Emergency Archive solution.

.DESCRIPTION
  Uses the dotnet tool pinned in .config/dotnet-tools.json (the official
  CycloneDX .NET tool) to produce a CycloneDX JSON SBOM of all solution
  dependencies into the sbom/ directory.

  The tool command name is resolved from the manifest, so the script never
  hard-codes it.

.PARAMETER OutputDirectory
  Directory that receives the SBOM file(s). Default: sbom.

.PARAMETER SkipToolRestore
  Skip 'dotnet tool restore' (use when tools are already restored).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\generate-sbom.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'sbom',
    [switch]$SkipToolRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Ensure a working SDK-capable dotnet (see setup-env.ps1 for the same logic).
$requiredSdk = (Get-Content (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
function Test-RequiredSdkAvailable {
    $list = & dotnet --list-sdks 2>$null
    return ($null -ne $list -and ($list -match [regex]::Escape($requiredSdk)))
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -or -not (Test-RequiredSdkAvailable)) {
    $userDotnetDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    if (Test-Path (Join-Path $userDotnetDir 'dotnet.exe')) {
        $env:Path = "$userDotnetDir;$env:Path"
    }
}
if (-not (Test-RequiredSdkAvailable)) {
    throw "A .NET SDK matching global.json ($requiredSdk) was not found. See docs\BUILD.md."
}

if (-not $SkipToolRestore) {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
}

# Resolve the tool command from the local tool manifest (SDK 10 creates it at
# the repository root; older layouts keep it under .config).
$manifestPath = Join-Path $repoRoot 'dotnet-tools.json'
if (-not (Test-Path $manifestPath)) {
    $manifestPath = Join-Path $repoRoot '.config\dotnet-tools.json'
}
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$commands = @($manifest.tools.PSObject.Properties.Value | ForEach-Object { $_.commands } | Where-Object { $_ })
if ($commands.Count -eq 0) { throw "No tool commands found in $manifestPath." }
$toolCommand = $commands[0]

# Fresh output directory (keep the README).
$outputPath = Join-Path $repoRoot $OutputDirectory
if (Test-Path $outputPath) {
    Get-ChildItem $outputPath -File |
        Where-Object { $_.Name -ne 'README.md' } |
        Remove-Item -Force
}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$version = (Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)
if ($version -match '<Version>([^<]+)</Version>') { $projectVersion = $Matches[1] } else { $projectVersion = '0.0.0' }

Write-Host "Generating CycloneDX SBOM with 'dotnet $toolCommand' (project version $projectVersion)..."

& dotnet tool run $toolCommand -- `
    (Join-Path $repoRoot 'EmergencyArchive.slnx') `
    --output-format Json `
    --output $outputPath `
    --set-name 'Emergency Archive' `
    --set-version $projectVersion `
    --set-type Application
if ($LASTEXITCODE -ne 0) { throw 'SBOM generation failed.' }

$bomFiles = Get-ChildItem $outputPath -File | Where-Object { $_.Name -ne 'README.md' }
Write-Host ''
Write-Host 'SBOM generated:' -ForegroundColor Green
$bomFiles | ForEach-Object { Write-Host ("  {0} ({1:N0} bytes)" -f $_.FullName, $_.Length) }
Write-Host 'Review the SBOM with any CycloneDX-compatible tool; keep it with every release (spec section 28, Phase 7).'
