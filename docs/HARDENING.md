# Hardening status (spec section 28, Phase 7)

Working checklist for the hardening pass. Anything not checked is either
scheduled or documented as a manual step for the archive owner.

| Item | Status | Where |
|---|---|---|
| SBOM generation | ✅ Done — `scripts/generate-sbom.ps1` → `sbom/bom.json` (CycloneDX 1.7, 48 components), regenerated per release | sbom/ |
| Cryptographic architecture review | ✅ Done — format decision + rationale + verified format summary | docs/CRYPTOGRAPHY.md |
| Static analysis | ✅ On by default — .NET analyzers, nullable reference types, `TreatWarningsAsErrors` across all projects | Directory.Build.props |
| Secret scanning | ✅ Automated — `scripts/scan-secrets.ps1` (private keys, hardcoded credentials, cloud tokens, hex blobs); allowlist documented in the script | scripts/scan-secrets.ps1 |
| Dependency audit | ✅ Automated check — `dotnet list EmergencyArchive.slnx package --vulnerable --include-transitive` reports **zero vulnerable packages** (verified 2026-09-06); central package pinning keeps the graph reviewable | Directory.Packages.props |
| Corruption testing | ✅ Automated — tampered headers/masterkey/config, truncated files, flipped bits in chunks, corrupted vault files detected and reported (Crypto + Search test suites) | tests/ |
| Power-loss / interruption testing | ✅ Automated — staged per-file writes (temp + atomic move), manifest written last as commit marker, interrupted-update idempotency test | tests/EmergencyArchive.Sync.Tests |
| Recovery testing | ✅ Automated + procedure — full vault round-trips, index rebuild after corruption (spec §24), recovery instructions deployed to `public\`; manual Cryptomator-app round-trip is a pre-release checklist item | docs/RECOVERY.md |

## Manual pre-release checklist

- [ ] Cryptomator interop: open a `VaultCli`-created vault in the official
      Cryptomator application and vice versa (docs/CRYPTOGRAPHY.md).
- [ ] Power-loss drill on real hardware: pull the drive during UPDATE ARCHIVE
      on the actual USB stick, confirm the previous version survives and the
      next update re-commits cleanly.
- [ ] Linux desktop smoke: run the published linux-x64 UI on a real Linux
      desktop (Docker validates the binaries; rendering needs a display).
- [ ] Re-run `scripts/scan-secrets.ps1` and the vulnerable-package audit
      before tagging a release; archive the SBOM with the release artifacts.

## Known limits (documented, not defects)

- UI-side 5-second password delay is anti-hammering only — offline attacks
  are stopped by scrypt cost + password strength (docs/THREAT-MODEL.md).
- Deterministic AES-SIV name encryption leaks name equality within a
  directory (inherent to the Cryptomator format).
- File sizes and directory fan-out are visible in ciphertext (inherent).
- CJK matching works per character after segmentation; trigram tokenizer is a
  possible future upgrade for longer substring semantics.
