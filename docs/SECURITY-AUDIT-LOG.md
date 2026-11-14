# Security Audit Log

A dated record of every security-verification run (dependency audit, secret
scan, test suite, SBOM) plus the release-record template. Re-run the commands
below before tagging any release and append a new entry — this file is the
paper trail that backs the claims in
[HARDENING.md](HARDENING.md) and [SECURITY-REVIEW.md](SECURITY-REVIEW.md).

## Repeatable commands

```powershell
# Dependency vulnerability audit (incl. transitive):
dotnet list EmergencyArchive.slnx package --vulnerable --include-transitive

# Secret scan:
powershell -ExecutionPolicy Bypass -File scripts/scan-secrets.ps1

# Full test suite (no host SDK needed — pinned container):
powershell -ExecutionPolicy Bypass -File scripts/build-in-docker.ps1 -Action test

# SBOM (CycloneDX 1.7):
powershell -ExecutionPolicy Bypass -File scripts/generate-sbom.ps1
```

## 2026-11-14 — post red/blue round 6 verification

| Check | Tool / command | Result |
|---|---|---|
| Dependency audit | `dotnet list package --vulnerable --include-transitive` (nuget.org advisory DB) | **Zero vulnerable packages** across all 14 projects, incl. transitive |
| Secret scan | `scripts/scan-secrets.ps1` | **Clean** — no candidates found |
| Test suite | `scripts/build-in-docker.ps1 -Action test` (pinned `mcr.microsoft.com/dotnet/sdk:10.0`) | **316 tests, 0 failures** (Integrity 10, Core 43, Search 46, Crypto 113, Sync 43, UI 60) |
| SBOM | `scripts/generate-sbom.ps1` | Regenerated — CycloneDX 1.7, **50 components** (`sbom/bom.json`, git-ignored; ship with the release) |
| Notes | — | Round 6 fixes live (KDBX hardening, scanner hardening, single-instance guard, password posture); Cryptomator interop drill and power-loss drill remain manual items |

## Release record template (copy per release)

```markdown
## Release vX.Y.Z — security record (YYYY-MM-DD)

| Check | Result |
|---|---|
| Dependency audit | <zero vulnerable / list> |
| Secret scan | <clean / findings> |
| Test suite | <N tests, 0 failures> |
| SBOM | CycloneDX 1.7, <N> components — attached to the release |
| Cryptomator interop drill | <passed — Cryptomator version X.Y.Z / pending> |
| Checksums + provenance | SHA256SUMS.txt + build-provenance attestation attached |
| Known limitations | See SECURITY-REVIEW.md residual-risk section (unchanged / updated) |
```
