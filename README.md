# Emergency Archive

A self-contained, offline, **encrypted emergency document archive** designed to
run from commodity 32 GB USB flash drives.

One application, one password, one search box — no Internet, no cloud account,
no external database, no installation:

> A user retrieves the USB during or after an emergency, plugs it into an
> available Windows or Linux computer, launches one application, enters one
> password, and immediately receives a simple searchable interface for locating
> important documents.

**Status: Phase 3 complete — archive builder, manifest, and verification.**
Environment, build, SBOM pipeline, USB layout, the Cryptomator format 8 vault
layer, full-text search (SQLite + FTS5 inside the encrypted vault), and the
archive update engine (source scanning, change detection, transactional
updates, Verify Archive) are in place with 168 passing tests. Setup Mode UI
arrives in Phase 4. See [CHANGELOG.md](CHANGELOG.md) and
[docs/CRYPTOGRAPHY.md](docs/CRYPTOGRAPHY.md) for the vault format decision.

The full design specification lives in
[Emergency Archive — LLM IDE Design Specification.md](Emergency%20Archive%20%E2%80%94%20LLM%20IDE%20Design%20Specification.md).

## Design principles (priority order, spec §2)

1. Data confidentiality
2. Long-term recoverability
3. Data integrity
4. Extremely simple emergency UX
5. Offline operation
6. Cross-platform portability

No custom cryptography is ever designed here (spec §5) — the vault will be a
documented, independently recoverable open-source format.

## Documentation

| Document | Purpose |
|---|---|
| [docs/USER-GUIDE.md](docs/USER-GUIDE.md) | How to use the archive in an emergency, and how to maintain it (Setup Mode) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Components, data flows, key design decisions |
| [docs/CRYPTOGRAPHY.md](docs/CRYPTOGRAPHY.md) | Vault format evaluation (Phase 0 draft) |
| [docs/THREAT-MODEL.md](docs/THREAT-MODEL.md) | What is protected — and what is not |
| [docs/RECOVERY.md](docs/RECOVERY.md) | Independent long-term recovery requirement |
| [docs/BUILD.md](docs/BUILD.md) | Dev environment, build, test, SBOM, USB deployment |

## Repository layout (spec §27)

```text
├── src/            EmergencyArchive.{UI,Core,Crypto,Search,Sync,Integrity}
├── tests/          matching xUnit test projects
├── docs/           architecture, threat model, crypto, recovery, build, user guide
├── scripts/        setup-env.ps1, generate-sbom.ps1, new-usb.ps1
├── sbom/           generated CycloneDX SBOM (per release)
├── EmergencyArchive.slnx   solution (new XML solution format, SDK 10 default)
├── global.json     pinned .NET SDK
└── Directory.*.props  shared build settings / pinned package versions
```

## Quick start (development)

Prerequisites: Windows 11 x64, git, and .NET SDK 10.0 (install instructions in
[docs/BUILD.md](docs/BUILD.md)).

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-env.ps1
```

This verifies the SDK, restores pinned tools and packages, builds, and runs the
tests. Manual equivalent:

```powershell
dotnet tool restore
dotnet build EmergencyArchive.slnx
dotnet test EmergencyArchive.slnx
dotnet run --project src\EmergencyArchive.UI
```

## SBOM

A CycloneDX SBOM is generated with the tool pinned in the local tool
manifest ([dotnet-tools.json](dotnet-tools.json)):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\generate-sbom.ps1
# → sbom\bom.json
```

Package versions are centrally pinned in [Directory.Packages.props](Directory.Packages.props)
with transitive pinning enabled, keeping the SBOM deterministic and reviewable.

## Target hardware (spec §3)

- Commodity 32 GB USB 3.x flash drive, exFAT for Windows/Linux interoperability.
- Reference drive in use: ORICO USB drive (~30 GB formatted ≈ 28.8 GiB), giving
  a 20–24 GB encrypted payload budget; the remainder is reserved for the
  application, index, update staging, and working space.
- Large photo/video libraries are out of scope; this is a document archive.

## Preparing the USB drive

```powershell
powershell -ExecutionPolicy Bypass -File scripts\new-usb.ps1 -DriveLetter D
```

Creates the on-drive layout from spec §4 (`app/`, `vault/`, `public/`,
`README.txt`, `public/RECOVERY-INSTRUCTIONS.txt`). It only adds files, refuses
the system drive, and requires `-Force` for a drive that already has content.
`START-WINDOWS.exe` is produced by the Phase 1 build.

## License

[MIT](LICENSE)
