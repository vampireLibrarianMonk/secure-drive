# Changelog

All notable changes to the Emergency Archive are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Phase 1 — Minimum Viable Vault**: the emergency screen now unlocks a real
  Cryptomator format 8 vault and shows the stored documents (name filter,
  OPEN via the host application, EXPORT / COPY to an encrypted-file-aware
  save dialog). LOCK (or closing the window) disposes the session, clears
  key material, and removes the per-session plaintext working folder used by
  OPEN, with the honest caveat that deletion is not forensic erasure.
- **5-second rate limit between password attempts** (spec §19):
  `AttemptRateLimiter` in Core, enforced by the UI with a visible countdown
  after every attempt. Documented as a UI-level anti-hammering convenience,
  not a security boundary (offline attacks target scrypt directly).
- `VaultLocator` — finds `vault\` per the spec §4 drive layout, with an
  `EMERGENCY_ARCHIVE_VAULT_PATH` development override (a path, not a secret).
- `VaultCli` tool (interim until Setup Mode, Phase 4): create/list/put/get
  for the archive owner, passwords prompted, never on the command line.
- **Phase 0 architecture decision**: the encrypted vault is the documented
  open-source **Cryptomator vault format 8** (SIV_GCM) — see
  docs/CRYPTOGRAPHY.md for the evaluation and rationale; independent recovery
  via the Cryptomator applications is documented in docs/RECOVERY.md and in
  the on-drive public/RECOVERY-INSTRUCTIONS.txt.
- Vault implementation in `EmergencyArchive.Crypto`: masterkey file
  (scrypt + AES-KW + versionMac), signed vault configuration (JWT), filename
  encryption (AES-SIV, NFC, base64url), flattened directory handling, 32 KiB
  AES-GCM content chunks, long-name shortening (.c9s), and a `VaultStore` /
  `VaultSession` API (create, unlock, list, read, write). Unit tests include
  RFC 5297 appendix A.1 conformance vectors, wrong-password, tampering,
  truncation, unicode, long-name, and multi-chunk round-trip coverage.
- Development environment: .NET SDK 10.0 pinned via `global.json`, central
  package management with transitive pinning, strict build settings
  (`TreatWarningsAsErrors`, nullable reference types, .NET analyzers).
- Solution scaffold per specification §27: `src/` (UI, Core, Crypto, Search,
  Sync, Integrity) and `tests/` (five xUnit projects) with Phase-0 domain
  foundations (archive versioning, categories, password policy, FTS5 query
  sanitization, SHA-256 hashing, sync plan model) and unit tests.
- Minimal Avalonia UI shell implementing the emergency startup screen
  (spec §7) with password field and unlock button (vault handshake pending
  Phase 1).
- SBOM pipeline: pinned CycloneDX .NET tool in the local tool manifest and
  `scripts/generate-sbom.ps1` producing CycloneDX JSON into `sbom/`.
- Environment bootstrap script `scripts/setup-env.ps1` (SDK check, tool
  restore, restore, build, test).
- USB deployment script `scripts/new-usb.ps1` creating the spec §4 drive
  layout with safety checks (system-drive guard, no deletion, `-Force`
  confirmation for non-empty drives).
- Documentation set: USER-GUIDE, ARCHITECTURE, CRYPTOGRAPHY (Phase 0 draft),
  THREAT-MODEL, RECOVERY, BUILD.
- Prepared the ORICO USB drive (D:) layout: `app/`, `vault/`, `public/`,
  `README.txt`, draft `public/RECOVERY-INSTRUCTIONS.txt`.

[Unreleased]: https://example.invalid/EmergencyArchive/compare/release-0.1.0...HEAD
