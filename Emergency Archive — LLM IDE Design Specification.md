# Emergency Archive

## 1. Mission

Build a self-contained, offline, encrypted emergency document archive designed to run from commodity 32 GB USB flash drives.

The primary use case is:

> A user retrieves the USB during or after an emergency, plugs it into an available Windows or Linux computer, launches one application, enters one password, and immediately receives a simple searchable interface for locating important documents.

The system must require:

- No Internet connection.
- No cloud account.
- No external database server.
- No installation where reasonably possible.
- No knowledge of encryption software by the end user.
- One password entry during normal operation.

The system should remain recoverable even if the Emergency Archive application itself is eventually unavailable.

---

# 2. Core Design Principles

Priority order:

1. Data confidentiality
2. Long-term recoverability
3. Data integrity
4. Extremely simple emergency UX
5. Offline operation
6. Cross-platform portability
7. Maintainability
8. Search performance
9. Visual polish

Do NOT invent cryptographic algorithms or proprietary encryption formats.

Use established, audited/open-source cryptographic libraries and documented formats.

---

# 3. Target Hardware

Initial target:

- Commodity 32 GB USB flash drives
- USB 3.x preferred
- exFAT host-visible filesystem preferred for Windows/Linux interoperability

Design usable archive capacity around approximately:

- 20–24 GB encrypted payload
- Remaining capacity reserved for application binaries, indexes, update operations, metadata, filesystem overhead, and free working space.

Large general-purpose photo/video libraries are explicitly outside the initial scope.

This is primarily a document archive.

---

# 4. Supported Platforms

Phase 1:

- Windows 11 x64

Phase 2:

- Linux x64

Preferred application stack:

- .NET
- C#
- Avalonia UI

Produce self-contained executables so the target computer does not require a preinstalled .NET runtime.

Suggested structure:

```text
/
├── START-WINDOWS.exe
├── START-LINUX
├── README.txt
│
├── app/
│   ├── windows/
│   ├── linux-x64/
│   └── resources/
│
├── vault/
│   └── [encrypted archive]
│
└── public/
    └── RECOVERY-INSTRUCTIONS.txt
```

Do not require OpenSearch, Elasticsearch, Java, Docker, PostgreSQL, or another server/service.

---

# 5. Encryption Architecture

## Critical Rule

Do not design custom cryptography.

Evaluate established open-source solutions/formats for the encrypted vault.

Candidates may include:

- Cryptomator-compatible vault
- VeraCrypt container
- age-based architecture
- another mature, documented, independently recoverable open-source format

The implementation must document why the selected approach was chosen.

### Requirements

Encryption must provide:

- Strong modern encryption.
- Password-based key derivation.
- Authenticated encryption/integrity protection where supported.
- Protection of document contents.
- Protection of filenames where practical.
- Protection of directory structure where practical.
- Resistance to offline password guessing using an appropriate memory-hard KDF.
- No plaintext password storage.
- No plaintext encryption keys written to the USB.

Argon2id should be preferred when compatible with the selected vault format.

### Single Entry Point

Normal operation must present ONE password prompt.

```text
Launch
   ↓
Password
   ↓
Key derivation / vault unlock
   ↓
Archive initialization
   ↓
Search UI
```

Do not require:

1. USB password
2. encryption password
3. application password

The archive password is the primary credential.

---

# 6. Independent Recovery Requirement

This is mandatory.

The encrypted documents MUST NOT become permanently dependent upon EmergencyArchive.exe.

If this project disappears 15 years from now, a technically competent person with:

- the USB,
- the password,
- standard/open-source recovery software,
- and `RECOVERY-INSTRUCTIONS.txt`

must be able to recover the documents.

Document the exact recovery procedure.

Include information identifying:

- Encryption format
- Format/version
- Compatible recovery software
- Files containing encrypted data
- How to unlock/decrypt manually

Do not put secret keys or the password in the recovery instructions.

---

# 7. Emergency Mode

This is the default application mode.

Startup screen:

```text
┌─────────────────────────────────────────┐
│                                         │
│          EMERGENCY ARCHIVE              │
│                                         │
│  Password                               │
│  ┌───────────────────────────────────┐  │
│  │ •••••••••••••••••               │  │
│  └───────────────────────────────────┘  │
│                                         │
│              [ UNLOCK ]                 │
│                                         │
└─────────────────────────────────────────┘
```

After successful unlock:

```text
┌─────────────────────────────────────────────────┐
│ EMERGENCY ARCHIVE                               │
│                                                 │
│ 🔎 Search [________________________________]   │
│                                                 │
│ Identity   Financial   Insurance   Property     │
│ Legal      Emergency   Education   Other        │
│                                                 │
│ Results                                         │
│ ─────────────────────────────────────────────── │
│ Homeowners Insurance.pdf                       │
│ Insurance • Updated 2026-08-14                  │
│                                                 │
│ Birth Certificate.pdf                           │
│ Identity • Updated 2025-03-02                   │
└─────────────────────────────────────────────────┘
```

The interface should be intentionally minimal.

A stressed, nontechnical family member should be able to operate it.

---

# 8. Search Architecture

Use:

**SQLite + FTS5**

Do not use OpenSearch.

Search index should support extracted text from common document types.

Initial formats:

- PDF
- TXT
- Markdown
- DOCX
- XLSX
- PPTX
- HTML

Optional later support:

- image OCR
- scanned PDF OCR

Searchable metadata should include:

- Filename
- Logical category
- Relative path
- Document title
- Extracted text
- File type
- File size
- Modification date
- Archive ingestion date
- SHA-256 hash

Example conceptual schema:

```text
documents
---------
id
logical_name
relative_path
category
mime_type
size
modified_at
indexed_at
sha256

document_text
-------------
document_id
title
body
```

Use FTS5 for full-text search.

Search must not require Internet access.

---

# 9. Plaintext Search Index Security

The search index may reveal extremely sensitive information.

Therefore:

**Never store the searchable index unencrypted outside the protected vault.**

The SQLite database belongs inside the encrypted archive.

If temporary plaintext files or indexes must exist during operation:

- Store them only in an ephemeral working location.
- Minimize their lifetime.
- Delete them during normal shutdown.
- Do not claim deletion guarantees forensic erasure on SSDs/flash storage.
- Never intentionally cache document contents in the public USB area.

Prefer architectures that avoid plaintext extraction to disk entirely.

---

# 10. Document Categories

Initial categories:

```text
Identity
Financial
Insurance
Property
Legal
Medical
Education
Emergency
Family
Other
```

Categories should be configurable.

Documents may optionally have multiple tags.

Do not require users to manually categorize every document.

---

# 11. Document Viewer Behavior

Search results should provide:

- Open
- Export/Copy
- Show metadata

Opening should normally use the operating system's registered application.

Do not attempt to build custom PDF/Office viewers during Phase 1.

Warn the user that opening a sensitive document in an external application can create temporary files, recent-file entries, thumbnails, or other traces on the host computer.

---

# 12. Setup / Administration Mode

Setup Mode is intended for the archive owner.

Access should occur after normal authentication.

Example:

```text
Settings
   ↓
Setup Mode
```

Setup dashboard:

```text
ARCHIVE SETUP

Documents:              3,842
Archive Size:           12.8 GB
Available USB Space:    15.1 GB
Last Update:            2026-09-04
Integrity:              HEALTHY

[ UPDATE ARCHIVE ]

[ Add Source ]
[ Remove Source ]
[ Rebuild Search Index ]
[ Verify Archive ]

[ Change Password ]
[ Export Recovery Instructions ]

[ Advanced ]
```

Do not expose unnecessary cryptographic controls to ordinary users.

---

# 13. Source Configuration

Allow Setup Mode to define source directories.

Example:

```text
Sources

C:\Users\User\Documents\Emergency
D:\Family\Taxes
D:\Family\Insurance
D:\Family\Property
D:\Family\Identity
```

Configuration should allow:

- Source path
- Logical category
- Include patterns
- Exclude patterns
- Recursive scanning

Example exclusions:

```text
*.tmp
~$*
Thumbs.db
.DS_Store
```

---

# 14. Archive Update Engine

Primary workflow:

```text
Configured Sources
        ↓
Scan
        ↓
Compare against manifest
        ↓
Detect:
   Added
   Changed
   Deleted
        ↓
Copy/update encrypted archive
        ↓
Extract searchable text
        ↓
Update SQLite FTS index
        ↓
Calculate SHA-256
        ↓
Update manifest
        ↓
Verify
        ↓
Commit successful update
```

Updates should be transactional where practical.

An interrupted update must not destroy the last known-good archive.

Prefer:

```text
PREVIOUS GOOD STATE
        ↓
STAGING UPDATE
        ↓
VERIFY
        ↓
COMMIT
```

If verification fails:

```text
ROLL BACK
```

---

# 15. Manifest

Maintain an integrity manifest.

For every archived file record:

```text
relative_path
size
modified_time
sha256
archive_version
```

The manifest itself must be protected against unauthorized modification, preferably by residing within authenticated encrypted storage and/or by an authenticated manifest design.

---

# 16. Verify Archive

Provide a prominent:

**VERIFY ARCHIVE**

function.

It should:

1. Enumerate every archived document.
2. Recalculate SHA-256.
3. Compare against manifest.
4. Verify database/index consistency.
5. Detect missing files.
6. Detect unexpected files where appropriate.
7. Produce a clear result.

Example:

```text
ARCHIVE VERIFICATION

Documents checked:     3,842
Data checked:          12.8 GB

Valid:                 3,842
Corrupt:               0
Missing:               0

Search database:       HEALTHY
Vault:                 HEALTHY

OVERALL STATUS:

✓ HEALTHY
```

Never report HEALTHY unless verification actually completed successfully.

---

# 17. Multiple USB Replicas

The architecture should assume multiple 32 GB USB drives can contain identical archive replicas.

Example:

```text
MASTER SOURCES
      │
 Archive Builder
      │
 ┌────┼────┐
 ▼    ▼    ▼
USB1 USB2 USB3
```

Possible physical deployment:

- USB #1 — home fire safe
- USB #2 — secure off-site location
- USB #3 — another geographically separated secure location

Each device is independently encrypted.

Do not assume RAID-like synchronization between USB devices.

The Setup application should eventually support:

**UPDATE THIS REPLICA**

and show archive version:

```text
Archive ID:
FamilyArchive

Archive Version:
2026.09.04.001

Generated:
2026-09-04 16:38

Status:
VERIFIED
```

---

# 18. Archive Versioning

Every completed update receives an immutable version identifier.

Suggested format:

```text
YYYY.MM.DD.sequence

2026.09.04.001
2026.09.04.002
```

Store:

- Archive ID
- Version
- Creation timestamp
- Application version
- Schema version
- Encryption format version
- Number of documents
- Total logical data size

---

# 19. Password Handling

Requirements:

- Never log passwords.
- Never persist passwords.
- Never include passwords in exception messages.
- Avoid immutable managed strings for secrets where practical.
- Clear sensitive buffers where supported.
- Do not pass passwords through command-line arguments.
- Do not expose password material through environment variables unless unavoidable and explicitly documented.
- Rate-limit attempts within the application.

Remember that an attacker possessing the USB can perform offline attacks against the encrypted archive. Application-side lockout alone is therefore not a security boundary.

Password strength and the vault's password KDF are critical.

---

# 20. Logging

Logging must NEVER contain:

- Passwords
- Encryption keys
- Extracted document contents
- Sensitive search queries by default
- Full sensitive filenames unless explicitly required

Operational logging may include:

```text
2026-09-04 14:32
Archive update started.

3842 documents scanned.
17 changed.
3 added.
1 removed.

Verification successful.

Archive version 2026.09.04.001 committed.
```

Logs containing archive metadata should preferably reside inside encrypted storage.

---

# 21. Emergency Locking

Provide:

**LOCK ARCHIVE**

On lock:

1. Close open archive database handles.
2. Stop background tasks.
3. Unmount/close encrypted storage.
4. Clear application-held secrets where possible.
5. Remove application-created temporary plaintext artifacts.
6. Return to password screen.

Application shutdown should perform the same process.

Unexpected termination must not corrupt the encrypted archive.

---

# 22. Read-Only Emergency Mode

Strongly consider making Emergency Mode read-only.

After unlock:

```text
EMERGENCY MODE
READ ONLY
```

Documents can be:

- searched
- viewed
- explicitly exported

But archive contents cannot accidentally be changed.

All archive mutation occurs through Setup Mode.

This reduces accidental corruption during emergencies.

---

# 23. Hostile/Unknown Computer Assumption

Assume that an emergency computer may be:

- borrowed
- shared
- untrusted
- compromised

The application cannot guarantee confidentiality once documents are decrypted on a compromised host.

Clearly document this limitation.

Never make claims such as:

> "No traces are left."

Instead minimize traces wherever reasonably possible.

---

# 24. Failure Modes

Design explicit handling for:

### Wrong Password

```text
Unable to unlock archive.

Check the password and try again.
```

Do not reveal unnecessary cryptographic information.

### Corrupt Vault

```text
Archive integrity problem detected.

Do not modify this USB.

Try another archive replica or follow
RECOVERY-INSTRUCTIONS.txt.
```

### Insufficient Space

Abort BEFORE putting the current known-good archive at risk.

### Interrupted Update

Recover last committed state.

### Corrupt Search Index

Documents must remain recoverable.

Offer:

```text
Rebuild Search Index
```

The index is disposable.

**Documents are authoritative; the search database is not.**

---

# 25. Threat Model

Protect against:

- Lost USB
- Stolen USB
- Casual unauthorized access
- Offline inspection of filesystem
- Offline password attacks
- Accidental modification
- File corruption
- Interrupted updates
- Search-index corruption
- Loss of EmergencyArchive application
- Failure of one USB replica

Not expected to fully protect against:

- Malware/keyloggers on the computer after password entry
- Sophisticated live-memory extraction
- Compromised operating system
- Physical destruction of every replica
- User choosing a weak password

Document these boundaries clearly.

---

# 26. Testing Requirements

Automated tests must cover:

### Encryption

- correct password
- incorrect password
- corrupted vault
- truncated vault

### Archive

- add file
- modify file
- delete file
- duplicate filename
- Unicode filename
- extremely long filename
- empty file
- large PDF

### Search

- exact filename
- partial filename
- full-text term
- phrase
- category filter
- no result
- Unicode content

### Integrity

- modified file
- missing file
- incorrect hash
- corrupted SQLite index

### Update Safety

Simulate termination during:

- scanning
- copying
- indexing
- hashing
- verification
- commit

The previous known-good archive must remain recoverable.

---

# 27. Repository Structure

Suggested repository:

```text
EmergencyArchive/
│
├── src/
│   ├── EmergencyArchive.UI/
│   ├── EmergencyArchive.Core/
│   ├── EmergencyArchive.Crypto/
│   ├── EmergencyArchive.Search/
│   ├── EmergencyArchive.Sync/
│   └── EmergencyArchive.Integrity/
│
├── tests/
│   ├── Core.Tests/
│   ├── Crypto.Tests/
│   ├── Search.Tests/
│   ├── Sync.Tests/
│   └── Integrity.Tests/
│
├── docs/
│   ├── ARCHITECTURE.md
│   ├── THREAT-MODEL.md
│   ├── RECOVERY.md
│   ├── CRYPTOGRAPHY.md
│   └── BUILD.md
│
├── scripts/
│
├── README.md
├── CHANGELOG.md
└── LICENSE
```

---

# 28. Development Phases

## Phase 0 — Architecture Decision

Before writing production code:

1. Compare Cryptomator, VeraCrypt, age, and other appropriate established open-source approaches.
2. Determine which can satisfy:
   - single-password UX
   - Windows
   - Linux
   - portable execution
   - no/low installation dependency
   - filenames protected
   - independent recovery
   - programmatic unlock
3. Write:

`docs/CRYPTOGRAPHY.md`

Do not proceed with custom cryptography.

---

## Phase 1 — Minimum Viable Vault

Implement:

```text
Launch
→ Password
→ Unlock
→ Display documents
→ Lock
```

Windows first.

---

## Phase 2 — Search

Implement:

- SQLite
- FTS5
- document metadata
- text extraction
- search UI

---

## Phase 3 — Archive Builder

Implement:

- source directories
- scanning
- incremental update
- SHA-256
- manifest
- verification
- transactional update

---

## Phase 4 — Administration

Implement:

- Setup Mode
- source management
- password change
- rebuild index
- verification
- archive health dashboard

---

## Phase 5 — Linux

Produce self-contained Linux x64 build from the same core codebase.

Avoid maintaining separate Windows/Linux business logic.

---

## Phase 6 — Replica Management

Implement archive IDs/versioning and simple replication/update support for multiple 32 GB USB drives.

---

## Phase 7 — Hardening

Perform:

- dependency audit
- static analysis
- secret scanning
- SBOM generation
- cryptographic architecture review
- corruption testing
- power-loss/interruption testing
- recovery testing

---

# 29. Definition of Done

A release is successful when this scenario works:

A nontechnical family member receives an encrypted 32 GB USB drive.

They plug it into a Windows computer with no Internet connection.

They launch:

`START-WINDOWS.exe`

They see one password field.

They enter the correct password.

Within a reasonable period they see:

```text
EMERGENCY ARCHIVE

Search: ___________________
```

They type:

```text
home insurance
```

The correct document appears.

They open or explicitly export it.

They close Emergency Archive.

The archive locks.

Someone subsequently obtaining the USB without the password cannot reasonably recover document contents or sensitive filenames.

Separately, if `START-WINDOWS.exe` no longer works years later, a technically competent person can follow `RECOVERY-INSTRUCTIONS.txt` and recover the encrypted documents using maintained open-source software.

That combination — **simple emergency operation + strong encryption + independent long-term recovery** — is the project's primary acceptance criterion.