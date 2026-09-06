# Cryptography — Vault Format Evaluation (Phase 0)

**Status: DRAFT — decision pending.** Per spec §28 (Phase 0), the vault format
must be selected and this document finalized **before** Phase 1 production
code. Custom cryptography is prohibited (spec §2, §5).

## Hard requirements (spec §5, §6, §28)

1. Strong modern encryption; password-based key derivation (Argon2id preferred).
2. Authenticated encryption / integrity protection where supported.
3. Protects contents; filenames and directory structure where practical.
4. Memory-hard KDF resistance to offline password guessing.
5. Single-password UX; programmatic unlock from our application.
6. Runs portably on Windows and Linux with no/low installation burden.
7. **Independent recovery**: unlockable years from now with maintained
   open-source software, following `RECOVERY-INSTRUCTIONS.txt`.
8. No plaintext keys or passwords written to the USB.

## Candidates

| Criterion | Cryptomator vault | VeraCrypt container | age-based archive |
|---|---|---|---|
| Model | Per-file encrypted vault (virtual drive or direct structure) | Single encrypted block container | File-based encryption tool |
| Filename/dir protection | Yes (name encryption is part of the format) | Yes (everything inside one container) | No — names visible unless individually wrapped; would need our own scheme |
| Single password UX | Yes | Yes | Yes (symmetric passphrase) |
| Programmatic unlock | Documented format + libraries; possible without mounting | Requires driver/mount, admin rights on host | Trivial (CLI/library) |
| Portable, no install | Good — format is documented; app integration possible | Mounting needs kernel driver/admin on the emergency host — poor fit for untrusted computers | Excellent — static CLI/libraries |
| Independent recovery | Mature third-party apps (Cryptomator, Cyberduck…) | Mature third-party (VeraCrypt itself) | Mature (age + GUIs) |
| KDF | Argon2id available in vault format | PBKDF2/whirlpool variants; memory-hardness weaker | scrypt (memory-hard) |
| Integrity/authenticity | Header + per-file AEAD | Container-level | Age encrypt (v1) lacks key-commitment…

## Open questions before deciding

1. Verify current format specifications and versions of each candidate
   (do not rely on memory; link exact specs in the final document).
2. Confirm filename encryption + programmatic (non-mounting) unlock for the
   chosen Cryptomator-compatible implementation, or pick an alternative that
   satisfies both.
3. Confirm behavior on read-only/locked-down hosts (no driver, no admin).
4. Prototype: create vault → unlock → add file → read back → recover with the
   independent tool, on Windows 11.
5. Decide the answer for "integrity of individual files" (manifest AEAD vs
   format AEAD).

## Decision

TBD — record: chosen format, format/version, reasons, rejected alternatives,
and the recovery procedure pointer. This decision gates Phase 1.
