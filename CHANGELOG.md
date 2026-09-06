# Changelog

All notable changes to the Emergency Archive are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Phase 4 — Administration / Setup Mode**: an owner-only dashboard after
  unlock (documents, archive size, free drive space, archive version, last
  update, integrity status), source management via folder picker (spec §13),
  UPDATE ARCHIVE (spec §14 engine with progress), VERIFY ARCHIVE (spec §16),
  REBUILD SEARCH INDEX (spec §24), password change (re-encrypts the
  masterkey file, policy-validated, atomic write), and recovery-instructions
  export to the drive's `public\` folder.
- **Phase 3 — Archive Builder**: source configuration stored inside the vault
  (spec §13, default exclusions `*.tmp`, `~$*`, `Thumbs.db`, `.DS_Store`,
  `desktop.ini`), source scanning with SHA-256/size/mtime capture, change
  detection against the manifest, and the transactional update engine
  (spec §14): staged per-file writes (temp + atomic move), deletions, and the
  new manifest written LAST as the commit marker — an interrupted update
  leaves the previous known-good archive intact and re-applies idempotently.
- **Verify Archive** (spec §16): re-hashes every stored document against the
  manifest, reports valid / corrupt / missing / unexpected files plus stale
  search-index entries, and reports HEALTHY only after a complete, successful
  check.
- **Incremental index updates** (gap closure from Phase 2): `SyncPlan`s are
  applied to the FTS5 index directly (added/changed re-extracted from the
  vault, deleted removed) instead of rebuilding.
- **CJK searchability** (gap closure): per-character segmentation of CJK runs
  in indexed bodies and queries, so substring searches like 证明 inside
  出生医学证明内容 match.
- **Archive manifest** (spec §15/§18): per-file SHA-256/size/mtime plus
  archive id and `YYYY.MM.DD.sequence` version with daily sequence, stored
  encrypted in the vault as the update commit marker.
- VaultCli owner commands: `sources`, `sources-add`, `update`, `verify`.
- **Phase 2 — Search**: SQLite + FTS5 index maintained entirely inside the
  encrypted vault (in-memory while unlocked; persisted as one encrypted file
  `index/search.index` — the plaintext index never touches disk, spec §9).
- Document text extraction for PDF (PdfPig), TXT, Markdown, DOCX, XLSX, PPTX
  (zip + hardened XML reader), and HTML; per-document extraction cap;
  unreadable documents fall back to name-only indexing.
- Search UI: search box (Enter / SEARCH button) over document names **and
  contents** with result snippets; the index builds in the background after
  unlock with progress feedback; a damaged index file is transparently
  rebuilt (spec §24 — documents are authoritative, the index is disposable).
- Search tests covering spec §26 behaviors (exact/partial filename, full-text
  term, phrase, category filter, no result, unicode content), an extractor
  suite including a programmatically constructed PDF, and an index-encryption
  assertion (no plaintext in any physical file, spec §9).
- **Phase 1 — Minimum Viable Vault**: the emergency screen unlocks a real
  Cryptomator format 8 vault and shows the stored documents (OPEN via the
  host application, EXPORT / COPY to a save dialog). LOCK (or closing the
  window) disposes the session, clears key material, and removes the
  per-session plaintext working folder used by OPEN, with the honest caveat
  that deletion is not forensic erasure.
- **5-second rate limit between password attempts** (spec §19):
  `AttemptRateLimiter` in Core, enforced by the UI with a visible countdown
  on every password entry path. Documented as a UI-level anti-hammering
  convenience, not a security boundary (offline attacks target scrypt
  directly).
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
  Sync, Integrity), `tools/VaultCli`, and `tests/` (five xUnit projects) with
  domain foundations (archive versioning, categories, password policy, FTS5
  query sanitization, SHA-256 hashing, sync plan model) and unit tests.
- SBOM pipeline: pinned CycloneDX .NET tool in the local tool manifest and
  `scripts/generate-sbom.ps1` producing CycloneDX JSON into `sbom/`.
- Environment bootstrap script `scripts/setup-env.ps1` (SDK check, tool
  restore, restore, build, test).
- USB deployment script `scripts/new-usb.ps1` creating the spec §4 drive
  layout with safety checks (system-drive guard, no deletion, `-Force`
  confirmation for non-empty drives).
- Documentation set: USER-GUIDE, ARCHITECTURE, CRYPTOGRAPHY (vault decision),
  THREAT-MODEL, RECOVERY, BUILD.
- Prepared the ORICO USB drive (D:) layout: `app/`, `vault/`, `public/`,
  `README.txt`, `public/RECOVERY-INSTRUCTIONS.txt`.

[Unreleased]: https://example.invalid/EmergencyArchive/compare/release-0.1.0...HEAD
