#Requires -Version 5.1
<#
.SYNOPSIS
  Scans the repository for accidentally committed secrets (spec section 28,
  Phase 7 hardening).

.DESCRIPTION
  Best-effort pattern scan over source, docs, and scripts (bin/obj/.git
  excluded). Reports candidates for: private key blocks, hardcoded
  password/key assignments, cloud/API credentials, and long hex blobs.
  Findings are a signal to review - not proof of a leak, and never a
  substitute for review discipline. Exit 0 = clean, 1 = candidates found.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\scan-secrets.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = ''
)

if ([string]::IsNullOrEmpty($RepoRoot))
{
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$ErrorActionPreference = 'Continue'

$excludeDirs = @('bin', 'obj', '.git', '.idea', 'artifacts', 'node_modules', 'sbom', 'tests')

# Allowlist notes (documented, reviewed):
# - sbom\: SBOM serial numbers and package hashes are hex by design.
# - tests\: synthetic passwords and known SHA-256 vectors only.

$includeExtensions = @('.cs', '.axaml', '.ps1', '.md', '.json', '.yml', '.yaml', '.sh', '.txt', '.xml', '.csproj', '.props', '.slnx')

$rules = @(
    @{ Name = 'private key block';       Pattern = '-----BEGIN [A-Z ]*PRIVATE KEY-----' },
    # Excludes XAML/binding expressions (e.g. RevealPassword="{Binding ...}") and
    # empty markup extensions — a real hardcoded secret is never a binding.
    @{ Name = 'hardcoded password';      Pattern = '(?i)(password|passwd|pwd)\s*[=:]\s*["''](?!\{)[^''"]{6,}["'']' },
    @{ Name = 'hardcoded api key';       Pattern = '(?i)(apikey|api[_-]key|secret[_-]key)\s*[=:]\s*["''][A-Za-z0-9+/_-]{12,}["'']' },
    @{ Name = 'aws access key';          Pattern = 'AKIA[0-9A-Z]{16}' },
    @{ Name = 'github token';            Pattern = 'gh[pousr]_[A-Za-z0-9]{20,}' },
    @{ Name = 'azure account key';       Pattern = '(?i)AccountKey=[A-Za-z0-9+/=]{40,}' },
    @{ Name = 'long hex blob';           Pattern = '"[0-9a-fA-F]{64,}"' }
)

$allFiles = Get-ChildItem $RepoRoot -Recurse -File -ErrorAction SilentlyContinue
$scanTargets = @()
foreach ($file in $allFiles)
{
    if ($file.FullName.Length -le $RepoRoot.Length) { continue }
    $relative = $file.FullName.Substring($RepoRoot.Length + 1)

    $excluded = $false
    foreach ($dir in $excludeDirs)
    {
        if ($relative -like "$dir\*" -or $relative -like "$dir/*") { $excluded = $true; break }
    }
    if ($excluded) { continue }
    if ($includeExtensions -notcontains $file.Extension.ToLowerInvariant()) { continue }

    $scanTargets += $file
}

$findings = 0
foreach ($file in $scanTargets)
{
    $relative = $file.FullName.Substring($RepoRoot.Length + 1)
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($file.FullName))
    {
        $lineNumber++
        foreach ($rule in $rules)
        {
            if ($line -match $rule.Pattern)
            {
                $findings++
                Write-Output ("  {0}:{1}: {2}" -f $relative, $lineNumber, $rule.Name)
            }
        }
    }
}

Write-Output ''
if ($findings -eq 0)
{
    Write-Output 'SECRET SCAN CLEAN - no candidates found.'
    exit 0
}

Write-Output "SECRET SCAN: $findings candidate(s) found - review each one."
exit 1
