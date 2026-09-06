# Threat Model

Status: living document. Baseline from spec §23–§25.

## Assets

- Document contents and sensitive filenames in the archive.
- The archive password (the single credential).
- The integrity of the archive (manifest, vault, search index).
- The ability to recover documents years later (independent recovery).

## Protected against

- Lost or stolen USB drive: strong encryption + memory-hard KDF; offline
  password guessing is expensive (and futile against a strong passphrase).
- Casual unauthorized access and offline filesystem inspection: no plaintext
  document data or sensitive filenames outside the vault; the SQLite index
  lives inside the vault (spec §9).
- Accidental modification: Emergency Mode is read-only; updates are staged,
  verified, then committed (spec §14, §22).
- File corruption and interrupted updates: SHA-256 manifest, transactional
  update, automatic recovery of the last known-good state; corrupt index is
  rebuildable (documents are authoritative).
- Loss of the Emergency Archive application itself: documented open-source
  vault format + recovery instructions on the drive (spec §6).
- Failure of one USB replica: multiple independent encrypted replicas.

## Not protected against (explicit limits)

- Malware or keyloggers on the computer **after** password entry.
- Compromised operating system or sophisticated live-memory extraction.
- Physical destruction of every replica.
- A weak password — the KDF raises cost, but a short password eventually falls
  to offline attacks.
- Traces left on a hostile host once a document is opened/exported there
  (thumbnails, recent files, temp files). We minimize traces; we never claim
  "no traces" (spec §23).

## Secret-handling rules (spec §19, §20)

- Never log or persist passwords/keys; never pass them via command line or
  environment variables; avoid immutable managed strings for secrets where
  practical; clear sensitive buffers; rate-limit unlock attempts in-app
  (while remembering that an attacker with the drive bypasses app lockout).
- Logs contain operational metadata only, and live inside encrypted storage.

## Assumptions

- The user guards the password; the drive may be lost.
- The emergency computer provides a working USB port and a standard OS —
  nothing more.
