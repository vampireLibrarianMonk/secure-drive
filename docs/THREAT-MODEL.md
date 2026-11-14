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
- **Password hammering through the application UI**: a minimum interval of
  5 seconds is enforced between password attempts, with a visible countdown
  (spec §19). This is a UI-level anti-hammering convenience only — it is NOT a
  security boundary. An attacker with the drive bypasses the application and
  attacks the scrypt key derivation offline; the password strength and KDF
  cost remain the real defense.
- Casual unauthorized access and offline filesystem inspection: no plaintext
  document data or sensitive filenames outside the vault; the SQLite index
  lives inside the vault (spec §9).
- **Hostile import files**: a malicious KeePass (`.kdbx`) file cannot make the
  importer spend unbounded CPU or memory — the KDF runs *before* the file is
  authenticated (that is exactly why it is an attack surface), so KDF
  parameters, header fields, decompression, and entry counts are capped before
  any of it executes, and the picked file is read with a bounded size.
- **Planted links and giant files in source folders**: symlinks/junctions
  inside a source directory are never followed (they could pull files from
  outside the source into the archive or create traversal cycles), and single
  files above the per-document size cap (2 GiB) are excluded from scanning and
  import.
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

- Never log or persist passwords/keys; never pass them via command line;
  clear derived key material and password byte-copies immediately
  (`DeriveKek`, `CompositeKey`, `VaultKeys` on dispose); rate-limit unlock
  attempts in-app (while remembering that an attacker with the drive bypasses
  app lockout by attacking the KDF offline).
- Logs contain operational metadata only, and live inside encrypted storage.

## Password-related attack surface (red/blue cycle, 2026-11)

- **Offline guessing is the primary attack.** scrypt (N=2²⁰, r=8) costs about
  1 GiB of RAM and roughly a second per guess on a fast CPU core; GPU rigs are
  memory-bandwidth-bound to tens of guesses per second. A four/five-word
  passphrase stands; a `Summer2024!`-style password does not. The in-app
  5-second pause adds nothing against this (documented above).
- **Copied secrets live on the host clipboard.** Any local application can
  read the clipboard, and Windows clipboard history (Win+V) may retain the
  entry — with cloud sync it can even leave the machine. Secret copies are
  now auto-cleared after 30 seconds; clipboard *history* still holds the
  older entry, so the user guide says to disable history on trusted hosts.
- **Passwords transit through managed strings.** The UI layer (text boxes)
  makes immutable strings unavoidable in Avalonia; derived byte copies are
  zeroized and entry boxes are cleared on lock, but a memory dump of a
  hostile host can still contain the password. A compromised host defeats
  the archive anyway; we minimize retention rather than claim erasure.
- **Exported KeePass files are their own attack surface.** Their KDF now
  matches the archive's posture (1 GiB Argon2id, 4 passes) instead of
  KeePassXC defaults, so the export is not the weakest link. The export
  file's password is a second secret to guard, and the file itself should be
  stored as carefully as the drive.
- **The app never verifies the "current password" on a change** — password
  change happens only inside an authenticated session, which is the
  authentication. There is no reset and no recovery.

## Assumptions

- The user guards the password; the drive may be lost.
- The emergency computer provides a working USB port and a standard OS —
  nothing more.
