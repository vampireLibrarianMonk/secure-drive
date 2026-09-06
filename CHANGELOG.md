# Changelog

All notable changes to the Emergency Archive are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
