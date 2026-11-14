# Hardening status (spec section 28, Phase 7)

Working checklist for the hardening pass. Anything not checked is either
scheduled or documented as a manual step for the archive owner.

| Item | Status | Where |
|---|---|---|
| SBOM generation | ✅ Done — `scripts/generate-sbom.ps1` → `sbom/bom.json` (CycloneDX 1.7, 48 components), regenerated per release | sbom/ |
| Cryptographic architecture review | ✅ Done — format decision + rationale + verified format summary | docs/CRYPTOGRAPHY.md |
| Static analysis | ✅ On by default — .NET analyzers, nullable reference types, `TreatWarningsAsErrors` across all projects | Directory.Build.props |
| Secret scanning | ✅ Automated — `scripts/scan-secrets.ps1` (private keys, hardcoded credentials, cloud tokens, hex blobs); allowlist documented in the script | scripts/scan-secrets.ps1 |
| Dependency audit | ✅ Automated check — `dotnet list EmergencyArchive.slnx package --vulnerable --include-transitive` reports **zero vulnerable packages** (re-verified 2026-11-14 incl. transitive; secret scan clean) ; central package pinning keeps the graph reviewable | Directory.Packages.props |
| Security audit log | ✅ Dated record of every verification run (dependency audit, secret scan, suite, SBOM) plus a per-release template | docs/SECURITY-AUDIT-LOG.md |
| Corruption testing | ✅ Automated — tampered headers/masterkey/config, truncated files, flipped bits in chunks, corrupted vault files detected and reported (Crypto + Search test suites) | tests/ |
| Power-loss / interruption testing | ✅ Automated — staged per-file writes (temp + atomic move), manifest written last as commit marker, interrupted-update idempotency test | tests/EmergencyArchive.Sync.Tests |
| KDBX import bounds | ✅ Automated — attacker-chosen KDF parameters (memory/passes/rounds/parallelism) capped before execution; header, VariantDictionary, block-stream, decompression, and entry-count bounds; hostile files fail fast (red/blue round 6) | tests/EmergencyArchive.Crypto.Tests/KdbxHardeningTests |
| Source-scan hardening | ✅ Automated — symlinks/junctions planted in source folders are never followed; per-document size cap (2 GiB) | tests/EmergencyArchive.Sync.Tests/SourceScannerHardeningTests |
| Vault path traversal | ✅ Automated — `..`, empty, and absolute-ish paths rejected; plaintext names/contents never reach the disk | tests/EmergencyArchive.Crypto.Tests/VaultPathHardeningTests |
| Single-instance guard | ✅ Automated — a second application instance opening the same archive is refused (`VaultInUseException`); concurrent cross-instance writes can no longer interleave | tests/EmergencyArchive.Crypto.Tests/VaultInstanceGuardTests |
| Release supply chain | ✅ Automated — every release publishes SHA256 checksums and a build-provenance attestation (verifiable with `gh attestation verify <file> -R <owner>/<repo>`) | .github/workflows/release.yml |
| Recovery testing | ✅ Automated + procedure — full vault round-trips, index rebuild after corruption (spec §24), recovery instructions deployed to `public\`; manual Cryptomator-app round-trip is a pre-release checklist item | docs/RECOVERY.md |

## Manual pre-release checklist

- [ ] Cryptomator interop drill (both directions — exact procedure below).
- [ ] Power-loss drill on real hardware: pull the drive during UPDATE ARCHIVE
      on the actual USB stick, confirm the previous version survives and the
      next update re-commits cleanly.
- [ ] Linux desktop smoke: run the published linux-x64 UI on a real Linux
      desktop (Docker validates the binaries; rendering needs a display).
- [ ] Re-run `scripts/scan-secrets.ps1` and the vulnerable-package audit
      before tagging a release; archive the SBOM with the release artifacts.

### Cryptomator interop drill (run before every release)

Prerequisites: the official Cryptomator application installed, the current
build (`scripts/publish.ps1` or the release ZIP), and the archive password.

**Direction 1 — our vault opens in Cryptomator:**

1. Create a scratch vault with our tooling:
   `dotnet run --project tools/VaultCli -- create <scratch-dir>`
   (or a temporary drive via `scripts/setup-drive.ps1 -DriveLetter E -SkipVault`,
   then the app's CREATE YOUR ARCHIVE screen).
2. Add one known document through `VaultCli put <scratch-dir> test.txt <file>`.
3. Open Cryptomator → *Add Vault* → *Open existing vault* → select
   `<scratch-dir>/masterkey.cryptomator` → enter the password → unlock.
4. Verify the document `test.txt` appears with its cleartext name and content.

**Direction 2 — a Cryptomator vault opens in our app:**

1. In Cryptomator, create a new vault (default settings) at `<dir2>` and copy
   a known file into the unlocked virtual drive.
2. Lock it in Cryptomator, then:
   `VaultCli list <dir2>` and enter that vault's password — the copied
   document must be listed.
3. Optionally point the UI at it (`EMERGENCY_ARCHIVE_VAULT_PATH=<dir2>`) and
   confirm browse/search/find.

**Pass criteria:** both directions list the expected documents with correct
names and readable contents; no errors in either application. Record the
Cryptomator version used in the release notes.

## Known limits (documented, not defects)

- UI-side 5-second password delay is anti-hammering only — offline attacks
  are stopped by scrypt cost + password strength (docs/THREAT-MODEL.md).
- Deterministic AES-SIV name encryption leaks name equality within a
  directory (inherent to the Cryptomator format).
- File sizes and directory fan-out are visible in ciphertext (inherent).
- CJK matching works per character after segmentation; trigram tokenizer is a
  possible future upgrade for longer substring semantics.
