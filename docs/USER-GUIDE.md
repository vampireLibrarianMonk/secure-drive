# Emergency Archive — User Guide

This guide is written for two people:

1. **The person in an emergency** who needs a document right now (Emergency Mode).
2. **The archive owner** who keeps the archive up to date (Setup Mode).

> **Implementation status (Phase 0):** this guide describes the finished
> product. Today the repository contains the environment, build pipeline, and
> UI shell; Emergency Mode arrives in Phase 1 and full Setup Mode in Phase 4.
> The guide is deployed to the drive (`app/resources/USER-GUIDE.md`) so it is
> always with the archive.

---

## 1. What this drive is

This USB drive contains your important documents — identity papers, insurance,
financial, property, legal, medical — **encrypted** so that losing the drive
does not expose them. Everything needs exactly one thing: **the archive
password**, given to you by the archive owner.

- No Internet is needed.
- Nothing is installed on the computer.
- The drive works on Windows (and, later, Linux).

---

## 2. Using the archive in an emergency (Emergency Mode)

1. **Plug the drive** into a computer you trust, if you have a choice.
2. **Open the drive** in File Explorer and run **`START-WINDOWS.exe`**.
3. **Type the archive password** into the single password field and press
   **UNLOCK**.
4. **Search.** Type what you are looking for in plain words — for example
   `home insurance` or `passport`. Results appear grouped by category
   (Identity, Financial, Insurance, Property, Legal, Medical, Education,
   Emergency, Family, Other).
5. **Open or export a document.** Select it and choose *Open* (opens with the
   computer's normal PDF/Office application) or *Export/Copy* to save a
   decrypted copy somewhere you choose (for example, the desktop or a printer).
6. **Lock when finished.** Click **LOCK** or simply close the application. The
   archive locks immediately and the password is forgotten.

### Wrong password and the 5-second pause

After **every** password attempt the application enforces a **5-second wait**
before the next attempt is allowed, with an on-screen countdown. Do not be
alarmed by the pause: it is a deliberate protection against someone trying
many passwords through the application. Take your time and enter the password
carefully.

### Rules for an emergency

- **Emergency Mode is read-only.** You cannot accidentally damage the archive;
  documents can only be searched, viewed, and explicitly exported.
- **Prefer a computer you trust.** On a borrowed or unknown computer, assume
  the computer itself may be watched. Opening or exporting a document may
  leave traces on that computer (recent-file lists, temporary files,
  thumbnails). This is unavoidable on a machine you do not control — keep
  emergency use on unknown computers as short as practical.
- **Do not modify anything on the drive** — do not rename, move, or delete
  files, and do not "clean up" the drive.
- **Remove the drive only after locking** the archive (or closing the
  application).

---

## 3. Keeping the archive current (Setup Mode — archive owner)

Setup Mode is reached from inside the application, **after** unlocking with
the archive password. Ordinary users never need it.

### Update the archive

After changing your source folders (the folders on your computer that hold the
real documents), choose **UPDATE ARCHIVE**. The update:

1. Scans the configured sources.
2. Detects added, changed, and deleted files.
3. Stages the update, verifies it, and only then commits it.

An interrupted update (power loss, unplugged drive) never destroys the last
known-good archive — the previous state is recovered automatically.

### Verify the archive

Run **VERIFY ARCHIVE** occasionally and after every major update. It checks
every document against its SHA-256 manifest entry and confirms the search
database is healthy. Only a completed, successful check may report
**✓ HEALTHY**.

### Change the password

Use **CHANGE PASSWORD**. Pick a long passphrase — see section 5. Changing the
password re-encrypts the vault; everyone who was given the old password needs
the new one.

### Manage replicas

Each 32 GB USB drive is an independent, fully encrypted replica. Update each
replica separately (**UPDATE THIS REPLICA**); the drive label shows the
archive ID and version (for example `2026.09.04.001`). Store replicas in
different locations (home safe, off-site, and so on).

### Export recovery instructions

**EXPORT RECOVERY INSTRUCTIONS** refreshes `public/RECOVERY-INSTRUCTIONS.txt`
on the drive. That file explains how a technically competent person can recover
the documents with standard open-source software if this application stops
working years from now. It never contains the password or any key.

---

## 4. Troubleshooting

| Message | What it means | What to do |
|---|---|---|
| *Unable to unlock archive. Check the password and try again.* | Wrong password. | Try again carefully. There is no password recovery: if the password is truly lost, the documents cannot be recovered. |
| *Archive integrity problem detected. Do not modify this USB.* | The encrypted vault or index is damaged. | Do not write to the drive. Try another replica if one exists; otherwise follow `public/RECOVERY-INSTRUCTIONS.txt`. |
| *Not enough space to complete the update.* | The sources grew past the drive capacity. | The update was aborted **before** touching the good archive. Reduce sources and update again. |
| *Search returns nothing.* | The search index may be damaged. | Use **Rebuild Search Index** in Setup Mode. Documents are safe — the index is disposable; the documents are authoritative, not the index. |

---

## 5. Choosing a good password

- **Length beats complexity.** Use at least **12 characters**; a passphrase of
  four or five random words (`river-copper-morning-lantern`) is both strong
  and memorable.
- This is the *only* key to the archive. An attacker who obtains the drive can
  try passwords offline indefinitely — a short password will eventually fall.
- **There is no password reset.** Store the password safely (password manager
  and/or paper in a secure place), and share it only with people who must be
  able to open the archive during an emergency.
- Never write the password on or with the drive, and never e-mail it together
  with the drive.

---

## 6. If the application stops working years from now

The archive format is a documented, independently recoverable open-source
format (chosen in Phase 0, documented in `docs/CRYPTOGRAPHY.md` and in
`public/RECOVERY-INSTRUCTIONS.txt` on the drive). A technically competent
person with the drive, the password, and maintained open-source software can
recover every document without this application.
