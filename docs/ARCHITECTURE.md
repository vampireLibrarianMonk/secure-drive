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
| `EmergencyArchive.Search` | FTS5 index (in-memory SQLite while unlocked, persisted encrypted inside the vault), document text extraction (PDF/TXT/MD/DOCX/XLSX/PPTX/HTML), safe query building. Composes Crypto (index persistence) and Integrity (hashing). |
| `EmergencyArchive.Sync` | Source scanning, change detection, transactional update staging (spec §14) |
| `EmergencyArchive.Integrity` | SHA-256, manifest, and archive verification (spec §15–16) |

Dependencies point inward: UI → Core; feature projects → Core; Core depends on
nothing in the solution.

## Key flows

### Update engine (Phase 3 — implemented)

```text
scan sources (spec §13: exclusions *.tmp, ~$*, Thumbs.db, .DS_Store…)
   → SHA-256 every source file → diff against manifest (SyncPlan)
   → apply added/changed with staged (temp + atomic move) writes
   → apply deletions → write new manifest LAST (commit marker)
   → apply the plan to the FTS5 index incrementally
```

- Interrupted update: the previous manifest stays the commit marker; the next
  update re-plans from it and re-applies changes idempotently (spec §14).
- The manifest (spec §15) lives encrypted in the vault at
  `manifest/archive-manifest.json` and carries the archive version
  `YYYY.MM.DD.sequence` (spec §18) and per-file SHA-256.
- Verify Archive (spec §16) re-hashes every document, reports valid / corrupt
  / missing / unexpected files and stale index entries, and only then reports
  HEALTHY.

### Setup Mode (Phase 4 — implemented)

The SETUP button (owner only, after unlock) opens the administration screen:

- Dashboard: archive id/version, document count, logical size, free drive
  space, last update, and the integrity status.
- UPDATE ARCHIVE: runs the Phase 3 engine with live progress, then applies
  the plan to the search index incrementally and refreshes the browse list.
- VERIFY ARCHIVE: re-hashes every document against the manifest and reports
  corrupt / missing / unexpected files plus stale index entries.
- REBUILD SEARCH INDEX: full index rebuild (spec §24 recovery path).
- Sources: add via folder picker / remove; stored encrypted in the vault.
- Change password: re-encrypts the masterkey file (policy-validated, atomic
  write). Applies to this replica — repeat on every replica (spec §17).
- Export recovery instructions: refreshes `public\RECOVERY-INSTRUCTIONS.txt`.

### Search (Phase 2 — implemented)

```text
unlock → index file present in vault?
   ├─ yes → decrypt index rows → in-memory SQLite → rebuild FTS5 → ready
   └─ no  → extract text per document (PDF/TXT/MD/DOCX/XLSX/PPTX/HTML)
            → hash (SHA-256) → store rows → rebuild FTS5 → encrypt & persist
query → Fts5Query.Sanitize (quoted prefix terms) → FTS5 MATCH
      → results with snippets, ordered by rank
```

- The plaintext index exists ONLY in memory while unlocked; the persisted
  index is a single encrypted vault file `index/search.index` (spec §9).
- A damaged index file is silently rebuilt from the documents — the index is
  disposable, documents are authoritative (spec §24).
- Text extraction is best-effort and capped; unreadable documents are indexed
  by name only. Categories default to the top-level folder (spec §10).

### Unlock (Phase 1/2 — implemented)

```text
Launch → single password prompt (5 s interval between attempts, spec §19)
       → scrypt KEK → AES-KW unwrap masterkeys → verify signed vault config
       → open vault → list documents (decrypted names)
       → load/build FTS5 search index in memory → Search UI (read-only)
```

One password, once (spec §5). Emergency Mode is read-only (spec §22); all
mutation happens in Setup Mode. OPEN exports a decrypted copy to a per-session
temp folder (removed on lock) and warns about host traces (spec §11/§23).

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
