# Build & Environment

## Prerequisites

- Windows 11 x64 (Phase 1 target; Linux x64 arrives in Phase 5).
- git.
- .NET SDK 10.0 — pinned in `global.json`.

### Installing the .NET SDK (either works)

```powershell
# Machine-wide (elevates):
winget install Microsoft.DotNet.SDK.10

# Or per-user, no admin required (official install script):
Invoke-WebRequest https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install.ps1"
powershell -ExecutionPolicy Bypass -File "$env:TEMP\dotnet-install.ps1" -Channel 10.0 -Quality GA
# Ensure %LOCALAPPDATA%\Microsoft\dotnet is on your PATH (user PATH).
```

This repository was bootstrapped with SDK **10.0.400** installed per-user at
`%LOCALAPPDATA%\Microsoft\dotnet` with the user PATH updated accordingly.

## Environment bootstrap

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-env.ps1
```

Restores pinned tools (`dotnet tool restore` from `dotnet-tools.json` at the
repository root), restores NuGet packages (`nuget.config`: nuget.org only),
builds (`TreatWarningsAsErrors` is on), and runs the tests.

Note: the scripts locate the SDK themselves — if the first `dotnet` on PATH is
a runtime-only host (common right after a per-user SDK install, before the
parent environment is refreshed), they fall back to
`%LOCALAPPDATA%\Microsoft\dotnet` automatically.

## Everyday commands

```powershell
dotnet build EmergencyArchive.slnx
dotnet test EmergencyArchive.slnx
dotnet run --project src\EmergencyArchive.UI        # emergency screen (unlock UI)
```

## Building without a host SDK (Docker)

If you have Docker but not the .NET SDK on the host, build and test inside the
pinned SDK container (the tag is read from `global.json`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-in-docker.ps1              # build
powershell -ExecutionPolicy Bypass -File scripts\build-in-docker.ps1 -Action test # build + full test suite
powershell -ExecutionPolicy Bypass -File scripts\build-in-docker.ps1 -Configuration Release -Publish linux-x64
```

This is a **build-time** convenience only — it does not change the shipped
drive, which stays SDK-free and Docker-free (the published binaries are
self-contained; see Publishing below). The repo is mounted so `bin/obj` land in
the tree just like a native build; NuGet packages stay inside the container. Use
`-SkipPull` when the image is already local. For the canonical **Windows** `.exe`,
still run `scripts\publish.ps1` on Windows. For the dedicated Linux validation
drill (staged copy + bare-Ubuntu self-contained smoke), use
`scripts\test-linux.ps1`.

## Creating the actual archive vault (interim, until Setup Mode)

Until Setup Mode ships in Phase 4, the archive owner creates the vault with
the command-line tool:

```powershell
dotnet run --project tools\VaultCli -- create D:\vault     # prompts for the password twice
dotnet run --project tools\VaultCli -- put D:\vault "Insurance/Home.pdf" "C:\Users\me\Documents\Home.pdf"
dotnet run --project tools\VaultCli -- list D:\vault
```

The result is a standard Cryptomator format 8 vault (see
docs/CRYPTOGRAPHY.md) — the owner can verify it by opening it with the
Cryptomator application. Passwords are prompted, never passed on the command
line (spec §19).

## SBOM

```powershell
powershell -ExecutionPolicy Bypass -File scripts\generate-sbom.ps1   # → sbom\bom.json
```

The CycloneDX tool is pinned in `dotnet-tools.json` at the repository root;
package versions are pinned in `Directory.Packages.props`.

## Preparing a USB drive (spec §4 layout)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\new-usb.ps1 -DriveLetter D
```

Adds (never deletes) `app/`, `vault/`, `public/`, `README.txt`,
`public/RECOVERY-INSTRUCTIONS.txt`, and a `.emergency-archive-drive.json`
marker. Refuses the system drive; `-Force` confirms non-empty drives.

## Publishing (Phase 5 — implemented)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

Produces self-contained single-file payloads for **both** platforms:

```text
artifacts\publish\windows\EmergencyArchive.UI\   win-x64 UI (START-WINDOWS.exe)
artifacts\publish\windows\VaultCli\              win-x64 owner CLI
artifacts\publish\linux\EmergencyArchive.UI\     linux-x64 UI
artifacts\publish\linux\VaultCli\                linux-x64 owner CLI
artifacts\publish\START-LINUX                    bash launcher (chmod on first run)
```

Deployment to a prepared drive (spec section 4): copy
`windows\EmergencyArchive.UI\EmergencyArchive.UI.exe` to the drive root as
`START-WINDOWS.exe`, copy `linux\EmergencyArchive.UI\*` to
`app\linux-x64\`, and `START-LINUX` to the drive root.

## Testing on Linux (spec section 28, Phase 5 — implemented)

`scripts/test-linux.ps1` validates the whole stack on real Linux via Docker:

1. Stages the source (bin/obj/.git excluded) into a temp folder.
2. Runs the **entire test suite** inside `mcr.microsoft.com/dotnet/sdk:10.0`
   (Ubuntu-based) — all vault crypto, sync, and search behavior on Linux.
3. Publishes VaultCli as a **self-contained linux-x64 single-file binary** and
   executes it inside the container, and again in a bare `ubuntu:24.04`
   container with **no .NET runtime** — proving the emergency-computer
   deployment model.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-linux.ps1
```

Requires Docker Desktop in Linux container mode. The same container command
is what a Linux CI runner would execute.

## UI tests (headless Avalonia)

`tests/EmergencyArchive.UI.Tests` renders the real `MainWindow` in Avalonia's
in-memory **headless** platform (no display needed) and asserts that exactly one
screen is visible in each application state — create / password / browse / setup.
These guard against screen-layering regressions (e.g. an `IsVisible` binding
evaluated against the wrong `DataContext`, which silently defaults a control to
visible and stacks two screens).

They run as part of the normal suite (`dotnet test EmergencyArchive.slnx`), in
`scripts\build-in-docker.ps1 -Action test`, and in CI. To run just them:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-ui.ps1 -SkipPull
```

## Pre-commit hooks

Install the shared git hooks once after cloning:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-hooks.ps1
```

This points `core.hooksPath` at the committed `scripts/hooks` directory. The
**pre-commit** hook runs two fast checks before a commit is created:

1. **Secret scan** (`scripts/scan-secrets.ps1`) — blocks committing credentials.
2. **Format/style** (`dotnet format --verify-no-changes`) — keeps the tree
   consistently formatted and analyzer-clean. Fix with
   `dotnet format EmergencyArchive.slnx` and re-stage.

Each check degrades gracefully if its tool is missing (e.g. no .NET SDK on a
Docker-only machine); CI still enforces both. Bypass once with
`git commit --no-verify` only for a confirmed false positive.

## Dependency & supply-chain currency

- Versions are centrally pinned in `Directory.Packages.props`; `nuget.config`
  restricts sources to nuget.org with package-source mapping.
- `.github/dependabot.yml` opens weekly PRs for NuGet and GitHub Actions,
  pinning actions to commit SHAs and keeping dependencies current.
- Audit locally:

  ```powershell
  dotnet list EmergencyArchive.slnx package --vulnerable --include-transitive
  dotnet list EmergencyArchive.slnx package --outdated
  ```

## Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request. It builds and
tests the solution inside the pinned `mcr.microsoft.com/dotnet/sdk:10.0`
container (Release, warnings-as-errors) and then runs the self-contained
`linux-x64` smoke test in a bare `ubuntu:24.04` image — the same steps as
`scripts\build-in-docker.ps1` and `scripts\test-linux.ps1`, so local and CI
results match.

## Troubleshooting

- *`dotnet` not found / wrong SDK*: new terminals after a user-local install
  need the updated PATH (sign out/in, or set it per session).
- *NUD errors on restore*: confirm `nuget.config` sources are reachable.
- *Build warns-as-errors*: fix the warning; do not downgrade the policy.
