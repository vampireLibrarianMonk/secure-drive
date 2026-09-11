 # Changelog

All notable changes to the Emergency Archive are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_Nothing yet._

## [0.3.0] - 2026-09-10

Security hardening release: an internal adversarial review pass (five rounds)
plus a due-diligence sweep. See [docs/SECURITY-REVIEW.md](docs/SECURITY-REVIEW.md)
for findings, fixes, and an honest residual-risk statement. Full suite: 215
tests, 0 failures.

### Added

- **Security review summary** (`docs/SECURITY-REVIEW.md`): what was verified
  sound, the findings fixed, and explicit residual risk.
- **Pre-commit hooks** (`scripts/hooks/pre-commit`, `scripts/install-hooks.ps1`):
  secret scan + `dotnet format --verify-no-changes` before each commit.
- **Dependabot** (`.github/dependabot.yml`): weekly NuGet + GitHub Actions
  updates; pins actions to commit SHAs.
- **Diagnostic logging** (`AppLog`): handled exceptions are logged (full detail
  to trace; a sanitized event to the encrypted activity log) instead of being
  silently swallowed.

### Fixed (security)

- **Path traversal on OPEN**: the temporary file written when opening a document
  is now confined to a sanitized leaf name inside the working directory
  (blocks traversal, absolute paths, and reserved device names).
- **Decompression bomb**: OOXML text extraction is bounded (per-entry and total
  decompressed bytes); PDF page count is capped.
- **Verify robustness**: a corrupt/unreadable document is reported as corrupt
  and the scan continues, instead of aborting the whole verification.
- **Vault config JWT**: verification selects the HMAC named by the `alg` header
  (HS256/384/512) and still rejects any other; the raw masterkey copy is
  zeroized after use.

### Changed

- Safe in-minor dependency bumps: Avalonia 11.3.20 → 11.3.21,
  Microsoft.Data.Sqlite 10.0.11 → 10.0.12, Microsoft.NET.Test.Sdk 18.9.0 →
  18.10.0. (Zero vulnerable packages, including transitive.)
- Solution formatted to a consistent style (`dotnet format`).

## [0.2.0] - 2026-09-10

### Added

- **Downloadable releases**: a GitHub Actions release workflow
  (`.github/workflows/release.yml`) builds self-contained Windows and Linux
  packages on a version tag and attaches ready-to-use ZIPs to a GitHub Release.
  A `VERSION` file is the authoritative semantic version.
- **Role-based docs**: `START-HERE.md` routes users vs. developers; a
  plain-language `docs/USER-GUIDE.md` (with screenshot placeholders under
  `reference_images/`) covers download → set up the stick → password → add
  documents → emergency search. README stays developer-focused.
- **In-app first-run vault creation**: when a drive has no vault, the app shows
  a **CREATE YOUR ARCHIVE** screen (password + confirm, policy-checked) that
  creates the vault and unlocks straight in — no CLI needed
  (`VaultLocator.LocateCreatable`, `MainWindowViewModel.Create`).
- **Estate-planning quick setup** (owner): writes a plain-language letter to the
  family into an `Estate Plan` folder plus a password-free `ESTATE-PLAN-README`
  in `public/`. It no longer creates per-folder placeholder notes (those
  cluttered search); a re-run also removes any left by earlier builds.
- **Add individual documents**: pick one or more files and add them to a chosen
  category directly from Setup (indexed and searchable immediately), alongside
  the folder-source import. Individually-added files are marked
  `ManifestSource.Manual` and are preserved across folder-based updates.
- **Document CRUD in Setup**: the *Manage documents* card can rename a document,
  move it to another category, replace its contents, or delete it (two-step
  confirm). Each updates the vault, manifest, and search index together;
  rename/move preserve the document's provenance (ManifestSource).
- **Headless UI tests** (`tests/EmergencyArchive.UI.Tests`, Avalonia.Headless):
  assert exactly one screen is visible per app state, guarding against
  screen-layering/binding regressions.
- **Container build/CI tooling**: `scripts/build-in-docker.ps1` and
  `scripts/test-ui.ps1` build and test in the pinned .NET SDK container (no host
  SDK needed); `scripts/setup-drive.ps1` prepares a drive end-to-end (layout +
  app + vault); `.github/workflows/ci.yml` runs build/test + a self-contained
  Linux smoke on every push/PR.

### Changed

- **Setup Mode redesign**: a fixed header, numbered task cards
  (1 Your documents, 2 Manage documents, 3 Estate planning, 4 Maintenance,
  5 Security), and the activity log docked in its own scroll region so it is
  never clipped. Activity entries show a category chip and formatted timestamp.
- The last **VERIFY** result is now remembered and shown at the top of Setup
  ("Last check (date): …") instead of resetting to "Not verified yet" each time
  Setup is re-entered.

### Fixed

- Vault **infrastructure files** (search index, `operations.log`,
  `sources.json`, manifest) no longer appear in the document browse/search list.
- Folder **UPDATE no longer deletes individually-added documents**; manual
  entries are excluded from source reconciliation and preserved on commit.
- Corrected a Setup screen binding where the setup view overlapped the password
  screen (`IsVisible` had resolved against the wrong `DataContext`), and a
  missing change-notification that could leave the browse screen blank after
  unlocking.
- Primary buttons center their label reliably (shared `Button.primary` style).

- **Phase 7 hardening** (spec §28): secret scanner (`scripts/scan-secrets.ps1`
  — clean), dependency audit (zero vulnerable packages incl. transitive),
  hardening checklist mapping every §28 item to its implementation
  (docs/HARDENING.md); corruption/interruption/recovery tests were already in
  place from Phases 2-3.
- **Phase 6 — Replica management** (spec §17): monotonic manifest revisions +
  content hashes enable objective replica comparison (in-sync / newer /
  diverged / different archives); `ReplicaInspector`/`ReplicaComparer` in
  Sync, `VaultCli replica(s)` commands for multi-drive inspection, Setup
  dashboard revision display, and automatic drive-marker refresh on update.
- **Phase 5 — Linux support**: `scripts/test-linux.ps1` runs the entire test
  suite plus a self-contained-deployment smoke test inside Docker (Ubuntu
  .NET SDK image + a bare `ubuntu:24.04` runtime-less container);
  `scripts/publish.ps1` produces self-contained single-file payloads for
  Windows and Linux (UI + VaultCli) and the `START-LINUX` launcher. The full
  suite passes on real Linux; clean-container builds caught and fixed latent
  Windows-only incremental-build defects.
- **Operational logging + activity view** (spec §20): every operation
  (unlock/lock, update commit, verify, index rebuild, source changes,
  password change, open/export) is recorded in a bounded (500-entry) log
  persisted **encrypted inside the vault** (`logs/operations.log`) and shown
  live in Setup Mode's activity list. Red lines enforced: no passwords, no
  key material, no raw search queries in the log. VaultCli writes the same
  log.
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
