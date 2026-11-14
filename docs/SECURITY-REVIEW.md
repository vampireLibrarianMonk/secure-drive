# Security Review — Emergency Archive

This records an internal adversarial review pass (five focused rounds plus a
due-diligence sweep), a **red/blue cycle over the post-0.4.0 surfaces**
(round 6), and, importantly, an **honest statement of residual risk**.
It is not a substitute for an independent third-party audit.

Scope reviewed at commit-level with tests added for each fix. As of round 6
the full suite is **310 tests, 0 failures** across crypto, integrity, sync,
search, and headless UI.

---

## What was verified as sound

- **Cryptography follows the documented Cryptomator format 8** (verified against
  the published spec), so no custom cryptography is trusted: scrypt KEK
  (N=2²⁰, r=8, p=1), AES Key Wrap for the masterkeys, AES-GCM file content in
  32 KiB chunks with per-chunk AAD binding the chunk index and header nonce,
  AES-SIV for names/dir-IDs, and a masterkey-signed vault-config JWT.
- **Constant-time comparisons** for the version MAC, config signature, and SIV
  synthetic IV; **key material is zeroized** (KEK, content keys, masterkey
  copies, `VaultKeys` on dispose).
- **Tamper/downgrade caught at unlock**: the vault config signature, format,
  cipher, and key-id are verified immediately after unwrapping the keys.
- **No injection / traversal / XXE reachable**: FTS5 queries are fully quoted,
  path segments reject `.`/`..` and are encrypted before touching the disk,
  and the OOXML XML reader disables DTDs and external resolution.

## Findings fixed in this pass

| # | Severity | Area | Fix |
|---|---|---|---|
| 1.3 | Low | Vault config JWT | Verify with the HMAC named by `alg` (HS256/384/512); still rejects any other alg. Zeroize the raw-masterkey copy after signing/verifying. |
| 2.1 | Medium | Integrity verify | A document that fails to open/decrypt is reported **corrupt** and the scan continues, instead of aborting the whole VERIFY. |
| 3.1 | Medium | Text extraction | Bound OOXML decompression (64 MB/entry, 128 MB total) to defeat a zip-bomb document; degrades to index-by-name-only. |
| 3.2 | Low | Text extraction | Cap PDF pages scanned (5000) to bound a hostile huge-page-count PDF. |
| 4.1 | Medium | Host / OPEN | Sanitize the (decrypted) document name to a safe leaf and confirm the OPEN temp path stays inside the working directory (blocks traversal, absolute paths, and Windows reserved device names). |
| 5.1 | Medium | Supply chain | Dependabot for NuGet + GitHub Actions (SHA-pins actions, keeps deps current). |

Due diligence also: **zero vulnerable packages** (incl. transitive), tree is
`dotnet format`-clean, safe in-minor dependency bumps applied, container SDK
image confirmed current, and a pre-commit hook (secret scan + format) added.

---

## Round 6 — red/blue cycle (2026-11-13)

A red/blue pass over the surfaces added since the last review — KDBX
import/export, the update engine's filesystem behavior, and the deployment
scripts. Method: enumerate attack vectors against the actual code, verify
each in source, fix, then exercise the fix with adversarial tests. Findings
fixed with tests:

| # | Severity | Area | Finding → Fix |
|---|---|---|---|
| 6.1 | Medium | KDBX import | The KDF runs **before** the file is authenticated, and attacker-chosen parameters were unbounded (`iterations`/`memory`/`parallelism`/KDBX3 `TransformRounds` could freeze the importer for hours or OOM it) → caps enforced before execution: Argon2 ≤ 2 GiB memory, ≤ 64 passes, ≤ 64 parallelism; AES-KDF ≤ 10M rounds. |
| 6.2 | Low | KDBX import | Unbounded pre-authentication allocations (header TLV fields, VariantDictionary values, HMAC block stream total, XML entry count) → bounded (16 MiB / 1 MiB+1024 entries / 1 GiB / 100k). Decompression was already capped at 128 MiB. |
| 6.3 | Low | KDBX import | The picked `.kdbx` was read into memory with no limit → 512 MiB bounded read in the UI. |
| 6.4 | Medium | Source scanning | Symlinks/junctions planted inside a source folder were followed: a planted link could pull files from **outside** the source into the archive, or create traversal cycles (DoS) → reparse points are skipped during enumeration and again per file. |
| 6.5 | Low | Size policy | No per-file size policy: a multi-gigabyte source file (or a link to one) would be hashed for hours and could exhaust the drive → 2 GiB per-document cap in the scanner and the single-file add path. |
| 6.6 | Medium | Deployment | `setup-drive.ps1` re-deploys nested the Linux payload one level deeper each time, and the copied `START-LINUX` launcher pointed at the build machine's artifacts layout (broken on the stick) → payload copied as contents; launcher generated for the drive layout. |
| 6.7 | Low | Estate letter | Letter saves did not update the archive manifest → VERIFY ARCHIVE reported the letter as an "unexpected" file; manifest is now kept in sync. |

Adversarial tests added: `KdbxHardeningTests` (fast-rejection of hostile KDF
parameters and oversized structures), `VaultPathHardeningTests` (traversal
rejection and plaintext-never-on-disk, re-verified), `SourceScannerHardeningTests`
(real symlink and sparse-oversized-file scenarios in the Linux container).

---

## Residual risk — what this does NOT protect against

Being direct about the limits is part of the security posture (see also
[THREAT-MODEL.md](THREAT-MODEL.md)):

- **The password is the only secret.** Anyone who obtains the drive can attack
  the vault **offline**, bypassing the app entirely. The 5-second in-app pause
  is anti-hammering only — it is **not** a security boundary. Strength rests
  entirely on password length and the scrypt cost parameters. A weak password
  will eventually fall. **There is no password recovery.**
- **A compromised host computer defeats the archive.** Malware, a keylogger, or
  another admin user on the machine can capture the password as you type it or
  read decrypted documents from memory or from the temporary files created when
  you **OPEN** a document. Opening also leaves normal host traces
  (recent-files, thumbnails). Prefer a computer you trust; the app warns on OPEN.
- **Windows executables are unsigned.** Users will see a SmartScreen
  "unknown publisher" warning; a signing certificate is required to remove it.
  Downloaded binaries currently carry no build-provenance attestation.
- **Tamper detection is after the fact.** The app detects a corrupted/tampered
  vault (authentication fails; VERIFY reports it) but cannot prevent someone
  with drive access from destroying or replacing data. Keep multiple replicas.
- **Metadata is not hidden.** The Cryptomator format encrypts file names and
  contents, but the number of files, their approximate sizes, and the directory
  structure shape are observable to someone with the drive.
- **This review is internal.** It reduces risk and documents assumptions; it is
  not an independent cryptographic audit or penetration test.

## Recommended next steps (not yet done)

- Code-sign the Windows build (removes SmartScreen; a signing step is stubbed in
  `release.yml`).
- Add build-provenance attestation to releases so downloads are verifiable.
- Commission an independent third-party security audit before promoting to a
  `1.0.0` "rely on this" release.
