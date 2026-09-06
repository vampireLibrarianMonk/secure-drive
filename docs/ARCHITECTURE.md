# Architecture

Status: living document for the Phase 0 scaffold. Update with each phase.

## Stack

- .NET 10 (LTS), C# — self-contained executables so target computers need no
  preinstalled runtime (spec §4).
- Avalonia UI — one codebase for Windows 11 (Phase 1) and Linux x64 (Phase 5).
- SQLite + FTS5 — embedded full-text search; no server of any kind (spec §8).
- The encrypted vault is an established open-source format (evaluation in
  [CRYPTOGRAPHY.md](CRYPTOGRAPHY.md)); no custom cryptography, ever (spec §5).

## Projects (spec §27)

| Project | Responsibility |
|---|---|
| `EmergencyArchive.UI` | Avalonia front-end: Emergency Mode (read-only search) and Setup Mode |
| `EmergencyArchive.Core` | Domain model: archive identity/versioning, categories, shared abstractions |
| `EmergencyArchive.Crypto` | Vault integration + password policy; delegates to established formats |
| `EmergencyArchive.Search` | SQLite/FTS5 index and query building (index lives inside the vault, spec §9) |
| `EmergencyArchive.Sync` | Source scanning, change detection, transactional update staging (spec §14) |
| `EmergencyArchive.Integrity` | SHA-256, manifest, and archive verification (spec §15–16) |

Dependencies point inward: UI → Core; feature projects → Core; Core depends on
nothing in the solution.

## Key flows

### Unlock (Phase 1)

```text
Launch → single password prompt → KDF/vault unlock → open vault
       → open (in-vault) SQLite index → Search UI (read-only)
```

One password, once (spec §5). Emergency Mode is read-only (spec §22); all
mutation happens in Setup Mode.

### Update (Phase 3, spec §14)

```text
scan sources → diff against manifest → SyncPlan (added/changed/deleted)
→ stage into vault → extract text → update FTS index → hash (SHA-256)
→ verify → commit (atomic)   |   verify fails → roll back
```

Interrupted updates recover the last committed state; the known-good archive
is never at risk mid-update.

### Verification (spec §16)

Every document re-hashed and compared to the manifest, plus index/vault
consistency. HEALTHY is only ever reported from a completed successful run.

## Invariants

1. The search index and all temporary plaintext live **inside** the encrypted
   vault (spec §9) — never in a public area of the USB.
2. Documents are authoritative; the search index is disposable and rebuildable.
3. Passwords/keys are never logged, persisted, or passed via command line
   (spec §19); secrets are cleared as soon as possible.
4. Every release ships with a CycloneDX SBOM and updated recovery instructions.
