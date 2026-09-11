# Emergency Archive

> 👋 **Not a developer? Just want to use the app?**
> This README is for people building the code. If you only want to put the app
> on a USB stick and use it, start at **[START-HERE.md](START-HERE.md)** →
> **[User Guide](docs/USER-GUIDE.md)**. No programming required.

*(Developer overview below.)*

---

A self-contained, offline, **encrypted emergency document archive** designed to
run from commodity 32 GB USB flash drives.

One application, one password, one search box — no Internet, no cloud account,
no external database, no installation:

> A user retrieves the USB during or after an emergency, plugs it into an
> available Windows or Linux computer, launches one application, enters one
> password, and immediately receives a simple searchable interface for locating
> important documents.

**Status: Phases 0-7 implemented — hardening pass included.**
Environment, build, SBOM pipeline, USB layout, the Cryptomator format 8 vault
layer, full-text search (SQLite + FTS5 inside the encrypted vault), the
archive update engine (source scanning, change detection, transactional
updates, Verify Archive), Setup Mode administration, Linux x64
self-contained deployment (Docker-validated), replica management, and the
hardening pass (secret scanning, dependency audit, hardening checklist) are
in place — 157+ tests green on both Windows and Linux. Remaining manual
items: Cryptomator interop drill and the pre-release checklist in
[docs/HARDENING.md](docs/HARDENING.md). See [CHANGELOG.md](CHANGELOG.md).

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

| Document | Audience | Purpose |
|---|---|---|
| [START-HERE.md](START-HERE.md) | Everyone | Router: sends users vs. developers to the right guide |
| [docs/USER-GUIDE.md](docs/USER-GUIDE.md) | Users | Plain-language: download, set up the stick, set a password, add documents, find them in an emergency |
| [docs/FIRST-USE.md](docs/FIRST-USE.md) | Helpers/IT | Command-line drive setup (e.g. D:), where things live, and reset |
| [docs/BUILD.md](docs/BUILD.md) | Developers | Environment, build, test, container build, publish, SBOM |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Developers | Components, data flows, key design decisions |
| [docs/CRYPTOGRAPHY.md](docs/CRYPTOGRAPHY.md) | Developers | Vault format evaluation (Phase 0 draft) |
| [docs/THREAT-MODEL.md](docs/THREAT-MODEL.md) | Developers | What is protected — and what is not |
| [docs/SECURITY-REVIEW.md](docs/SECURITY-REVIEW.md) | Developers | Adversarial review findings/fixes and honest residual risk |
| [docs/RECOVERY.md](docs/RECOVERY.md) | Developers | Independent long-term recovery requirement |

## Repository layout (spec §27)

```text
├── src/            EmergencyArchive.{UI,Core,Crypto,Search,Sync,Integrity}
├── tests/          matching xUnit test projects
├── docs/           architecture, threat model, crypto, recovery, build, user guide
├── scripts/        setup-env.ps1, build-in-docker.ps1, test-ui.ps1, publish.ps1, new-usb.ps1, setup-drive.ps1, generate-sbom.ps1
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

No .NET SDK on the host but have Docker? Build and test in the pinned SDK
container instead (see [docs/BUILD.md](docs/BUILD.md)):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-in-docker.ps1 -Action test
```

## Releases (downloadable packages)

Pushing a version tag builds ready-to-use Windows and Linux packages and
attaches them to a GitHub Release
([`.github/workflows/release.yml`](.github/workflows/release.yml)). Each package
is a self-contained app laid out for a USB stick (spec §4) — a user extracts the
ZIP onto the stick and runs the launcher; the app creates the vault on first
launch. No .NET runtime is needed on the target machine.

The authoritative version is the first line of the [`VERSION`](VERSION) file,
kept in sync with `<Version>` in `Directory.Build.props` and the top
[CHANGELOG](CHANGELOG.md) entry. To cut a release:

```powershell
# 1. bump VERSION (first line) + <Version> in Directory.Build.props
# 2. move CHANGELOG [Unreleased] into a new [x.y.z] section
git tag v0.2.0
git push origin v0.2.0     # triggers the build-and-release workflow
```

Enable it once per repo: **Settings → Actions → General → Workflow permissions →
Read and write permissions** (lets the workflow create the Release).
`scripts/assemble-release.ps1` builds the on-drive layout the workflow zips, and
can be run locally with `pwsh` to preview a package.

## Continuous integration

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds and tests every
push and pull request inside the pinned `mcr.microsoft.com/dotnet/sdk:10.0`
container (Release, warnings-as-errors), then publishes a self-contained
`linux-x64` VaultCli and runs it in a bare `ubuntu:24.04` image with **no .NET
runtime** — the same run-anywhere check as `scripts\test-linux.ps1`. No host SDK
is required; the shipped drive stays SDK-free and Docker-free.

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

**First-time setup of a drive (e.g. D:), end to end** — layout, application,
and the encrypted vault in one command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1          # build the app once
powershell -ExecutionPolicy Bypass -File scripts\setup-drive.ps1 -DriveLetter D
```

`setup-drive.ps1` scaffolds the layout, deploys `START-WINDOWS.exe`, and creates
the vault with `VaultCli create D:\vault` (prompting for the password). It only
adds files, refuses the system drive, needs `-Force` for a non-empty drive, and
skips vault creation if one already exists — safe to re-run. Full walkthrough in
[docs/FIRST-USE.md](docs/FIRST-USE.md).

> No command line for the vault? Run `setup-drive.ps1 -DriveLetter D -SkipVault`,
> then launch `START-WINDOWS.exe` — with no vault present the app shows a
> **CREATE YOUR ARCHIVE** screen that creates the vault for you.

**Layout only** (no app, no vault):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\new-usb.ps1 -DriveLetter D
```

Creates the on-drive layout from spec §4 (`app/`, `vault/`, `public/`,
`README.txt`, `public/RECOVERY-INSTRUCTIONS.txt`).

## License

[MIT](LICENSE)
