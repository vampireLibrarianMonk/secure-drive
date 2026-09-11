#Requires -Version 5.1
<#
.SYNOPSIS
  Installs the repository's git hooks by pointing core.hooksPath at the
  committed hooks directory (scripts/hooks).

.DESCRIPTION
  Uses git's core.hooksPath so the hooks are version-controlled and shared,
  rather than copying into .git/hooks (which is not tracked). Run once after
  cloning. The pre-commit hook runs a secret scan and a formatting check.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\install-hooks.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

git config core.hooksPath 'scripts/hooks'
if ($LASTEXITCODE -ne 0) { throw 'Failed to set core.hooksPath.' }

# Make the hook executable where the filesystem tracks that bit (Linux/macOS).
if ($IsLinux -or $IsMacOS) {
    chmod +x scripts/hooks/pre-commit
}

Write-Host "Installed git hooks (core.hooksPath = scripts/hooks)." -ForegroundColor Green
Write-Host "The pre-commit hook runs: secret scan + 'dotnet format --verify-no-changes'."
Write-Host "Bypass once with 'git commit --no-verify' only for a confirmed false positive."
