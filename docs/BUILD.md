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
dotnet build EmergencyArchive.sln
dotnet test EmergencyArchive.sln
dotnet run --project src\EmergencyArchive.UI        # emergency screen shell
```

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

## Publishing (from Phase 1)

```powershell
dotnet publish src\EmergencyArchive.UI -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output lands in `app/windows/` on the drive; Linux x64 equivalent in Phase 5
(`app/linux-x64/`, `START-LINUX` launcher).

## Troubleshooting

- *`dotnet` not found / wrong SDK*: new terminals after a user-local install
  need the updated PATH (sign out/in, or set it per session).
- *NUD errors on restore*: confirm `nuget.config` sources are reachable.
- *Build warns-as-errors*: fix the warning; do not downgrade the policy.
