# Cryptography — Vault Format Decision (Phase 0)

**Status: DECIDED (2026-09-06).** The vault format is the documented,
open-source **Cryptomator vault format 8** (`SIV_GCM`). No custom cryptography
is designed anywhere in this project (spec §2, §5); the format's primitives are
standard (AES-GCM, AES-SIV per RFC 5297, AES Key Wrap per RFC 3394, scrypt per
RFC 7914, HMAC-SHA-256) and are implemented on top of BouncyCastle and the
.NET crypto library, conformance-tested against RFC 5297 appendix A.1 vectors.

## Decision rationale

The vault format must satisfy (spec §5, §6, §28): single-password UX,
filename + directory-structure protection, memory-hard KDF, portable
execution with no/low installation, programmatic (non-mounting) unlock on
possibly untrusted computers, and independent recovery with maintained
open-source software.

| Criterion | **Cryptomator format 8 (chosen)** | VeraCrypt container | age-based scheme |
|---|---|---|---|
| Filename/dir-structure protection | Yes — AES-SIV name encryption, flattened hashed directories | Yes (all inside one container) | No — names visible; hiding them would require a custom scheme |
| Single password UX | Yes | Yes | Yes |
| Programmatic unlock without mounting/admin | Yes — documented format, read directly in-process | No — requires kernel driver + admin on the host | Yes |
| Memory-hard KDF | scrypt (parameters stored per vault; we raise them above defaults) | PBKDF2 only (not memory-hard) | scrypt |
| Integrity protection | Per-chunk AES-GCM + SIV authentication + signed vault config (JWT) + versionMac downgrade protection | Container-level | Header MAC |
| Independent recovery | Cryptomator apps (GPLv3, mature, Windows/Linux/macOS/Android/iOS) + documented manual procedure | VeraCrypt itself | age itself |
| Incremental updates of a 20–24 GB archive | Per-file encryption — only changed files are re-written | Whole-container rewrite for structural change | Per-file, but no name protection |

**Why not the alternatives:** VeraCrypt fails the hostile-computer requirement
(mounting needs admin/driver, spec §23) and lacks a memory-hard KDF. age has
no filename/structure protection; working around that means designing our own
container scheme, which spec §5 forbids in spirit. rclone was evaluated as an
independent-recovery tool but has no Cryptomator backend (verified against the
rclone backend list); recovery therefore relies on the Cryptomator
applications plus the documented format (see RECOVERY.md).

**KDF note (spec §5):** Argon2id is preferred "when compatible with the
selected vault format". Cryptomator's masterkey file format specifies scrypt,
so Argon2id is *not* compatible; scrypt is memory-hard and its cost parameters
are stored in the masterkey file. We deviate from Cryptomator's default
(N = 2^15, r = 8 ≈ 32 MiB) by using **N = 2^20, r = 8, p = 1 (≈ 1 GiB)** for
vaults we create, raising the offline-guessing cost substantially while
remaining fully format-compatible. Unlocking therefore needs ~1 GiB of free
RAM; Setup Mode can lower this for constrained hosts.

## Format summary (as implemented)

- `masterkey.cryptomator` — JSON: `version` (999, legacy), `scryptSalt`
  (8 random bytes), `scryptCostParam` (N), `scryptBlockSize` (r), wrapped
  `primaryMasterKey`/`hmacMasterKey` (AES-KW), `versionMac`
  (HMAC-SHA-256 over the big-endian version, keyed with the MAC masterkey).
  KEK = scrypt(password, salt‖pepper, N, r, p=1, 32 bytes).
- `vault.cryptomator` — JWT (HS-256, key = encMasterKey‖macMasterKey):
  `format` 8, `cipherCombo` `SIV_GCM`, `shorteningThreshold` 220, `jti` UUID.
- Names — NFC-normalized UTF-8, encrypted with AES-SIV
  (K = encMasterKey ‖ macMasterKey), parent directory ID as associated data,
  stored as padded base64url + `.c9r`; > 220 characters → `.c9s` directory
  (`name.c9s` mapping + `contents.c9r`).
- Directories — random UUID directory IDs (`dir.c9r` markers), flattened
  storage at `d/` + base32(sha1(aesSiv(dirId))) fan-out (2 + 30 chars).
- File contents — 68-byte header (12-byte nonce ‖ AES-GCM payload with the
  random per-file 32-byte content key ‖ 16-byte tag), then 32 KiB AES-GCM
  chunks: 12-byte random nonce ‖ ciphertext ‖ 16-byte tag with
  AAD = chunk index (64-bit BE) ‖ header nonce.

## Residual risks / follow-ups

- Interoperability test: open a vault created by this implementation with the
  official Cryptomator application, and vice versa (required before Phase 1
  release; the implementation follows the documented format).
- Formal review of the SIV usage (deterministic names leak name equality
  within a directory — inherent to the format, documented in the threat model).
- File sizes and directory fan-out leak metadata (inherent; spec §9).
