# Emergency Archive — First-Use & Directions

This guide gets a brand-new drive (for example the **D:** drive) ready and
explains where everything lives and how to use it. It is written for the
**archive owner** setting things up for the first time. If you are a family
member opening the archive in an emergency, jump to
[Using the archive](#using-the-archive-emergency-mode) or read
[USER-GUIDE.md](USER-GUIDE.md).

---

## 1. Where everything lives

The whole product runs **from the drive** — nothing is installed on the
computer. After setup, the drive (for example `D:\`) looks like this
(spec section 4):

```text
D:\
├── START-WINDOWS.exe          the application — double-click this
├── START-LINUX                the Linux launcher (if deployed)
├── README.txt                 plain-text overview, readable by anyone
├── app\
│   ├── windows\               START-WINDOWS.exe + VaultCli.exe (owner tool)
│   ├── linux-x64\             the Linux application
│   └── resources\             USER-GUIDE.md copied onto the drive
├── vault\                     the ENCRYPTED archive — never edit by hand
│   ├── masterkey.cryptomator  password-protected key material
│   ├── vault.cryptomator      signed vault configuration
│   └── d\                     the encrypted documents and folders
└── public\
    ├── RECOVERY-INSTRUCTIONS.txt   how to recover without this app (no secrets)
    └── ESTATE-PLAN-README.txt      what the drive is + who to call (no secrets)
```

Key idea: **the application finds the vault by looking for `vault\` next to
itself.** `START-WINDOWS.exe` sits at the drive root (and in `app\windows\`),
and the app walks up the folder tree looking for `vault\masterkey.cryptomator`.
So as long as the app and the `vault\` folder stay on the same drive, it just
works — no configuration, no paths to type.

> **Developers:** you can point the app at any vault with the
> `EMERGENCY_ARCHIVE_VAULT_PATH` environment variable (a path is not a secret).
> A ready-made demo vault ships at `artifacts\demo-vault` for testing.

---

## 2. Set up the D: drive for first use

There are two ways to prepare a drive. The simplest **creates the archive from
inside the app** — no command line needed.

### Option A — recommended: let the app create the archive

1. Put the layout and the app on the drive:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1                     # build the app once
   powershell -ExecutionPolicy Bypass -File scripts\setup-drive.ps1 -DriveLetter D -SkipVault
   ```

   `-SkipVault` prepares the folder layout and copies `START-WINDOWS.exe` to the
   drive, but leaves vault creation to the app.

2. Open `D:\` and run **START-WINDOWS.exe**. Because the drive has no archive
   yet, the app shows a **CREATE YOUR ARCHIVE** screen. Enter a password twice
   and click **CREATE ARCHIVE**. The app creates `D:\vault` and unlocks straight
   into the archive — then continue with [estate-planning setup](#3-quick-estate-planning-setup-owner).

### Option B — create everything from the command line

Do the whole thing in one command, including the vault:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1                 # build the app once
powershell -ExecutionPolicy Bypass -File scripts\setup-drive.ps1 -DriveLetter D
```

`setup-drive.ps1` does three things in order:

1. **Layout** — creates `app\`, `vault\`, `public\`, `README.txt`, and the
   recovery instructions (via `new-usb.ps1`).
2. **Deploy** — copies `START-WINDOWS.exe` (and `VaultCli.exe`) onto the drive
   from `artifacts\publish`. If you have not built yet, this step is skipped
   with a note and you can deploy later.
3. **Vault** — creates the encrypted vault with `VaultCli create D:\vault`,
   prompting you for the archive password **twice**.

Safety: the script only **adds** files, refuses the **system drive**, needs
`-Force` for a drive that already has content, and **skips vault creation if a
vault already exists** — so it is safe to re-run.

Useful switches:

- `-SkipApp` — prepare layout + vault, deploy the app yourself later.
- `-SkipVault` — prepare layout + app, create the vault in the app (Option A).
- `-Force` — allow a drive that already contains files (nothing is deleted).

If you only want the folder layout (no app, no vault), use `new-usb.ps1`
directly:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\new-usb.ps1 -DriveLetter D
```

### Starting over on a drive (reset)

To wipe an existing archive and set the drive up from scratch, add `-Reset`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-drive.ps1 -DriveLetter D -Reset
```

This **permanently deletes** `D:\vault\` (every archived document — there is no
password recovery) and `D:\public\`, then lays the drive out fresh so you can
create a new vault. It asks you to type the drive letter to confirm (skip the
prompt with `-Force`), refuses the system drive, and leaves any other files on
the drive untouched. Because it deletes only those two folders, you do not need
to reformat the drive.

### Choosing the password

This password is the **only** key to the archive. There is **no reset**.

- Use a long passphrase — four or five random words
  (`river-copper-morning-lantern`) beats a short complex one.
- Store it safely (password manager and/or paper in a secure place).
- Never write it on or with the drive, and never email it with the drive.

---

## 3. Quick estate-planning setup (owner)

Once the drive is ready, plug it in, run `START-WINDOWS.exe`, enter the
password, and click **SETUP**. The first card is **ESTATE PLANNING**:

1. Type **your name** (used in the letter to your family) and, optionally, a
   **contact for help** (an executor, lawyer, or trusted relative).
2. Click **WRITE ESTATE-PLANNING LETTER**. This:
   - writes a plain-language **letter to your family** into an `Estate Plan`
     folder inside the archive (encrypted, browsable immediately). The letter
     includes guidance on where each kind of document is filed — Identity,
     Financial, Insurance, Property, Legal, Medical, Family. Those folders
     appear on their own once you file real documents in them (no placeholder
     notes are created, so nothing clutters search);
   - writes a **password-free** `ESTATE-PLAN-README.txt` into `public\` so that
     whoever finds the drive learns what it is and who to call.
3. Add your documents, either way:
   - **Add individual files:** in the *2 · Your documents* card, choose a
     category, click **ADD FILE(S)…**, and pick one or more files. They are
     copied into the archive and searchable immediately — no folder or import
     step needed.
   - **Import whole folders:** click **ADD FOLDER…**, pick a folder that holds
     your documents, then click **IMPORT / UPDATE ARCHIVE**. Re-run IMPORT
     whenever those folders change.

That is the whole "pass it on" flow: your family gets a drive, a password, a
letter that explains everything, and folders that make sense.

---

## 4. Using the archive (Emergency Mode)

For the person who needs a document right now:

1. Plug the drive into a computer you trust and run **START-WINDOWS.exe**.
2. Type the archive password and press **UNLOCK**.
3. **Search** by name as you type, or press **Enter** to search inside the
   documents. Or browse the folders on the left.
4. Select a document and choose **OPEN** to view it or **EXPORT / COPY** to
   save a decrypted copy.
5. Click **LOCK** (or close the window) when finished.

Emergency Mode is **read-only** — you cannot damage the archive by looking
through it. After every password attempt there is a deliberate **5-second
pause**; that is normal.

See [USER-GUIDE.md](USER-GUIDE.md) for the full user guide and
[RECOVERY.md](RECOVERY.md) for recovering the documents years from now without
this application.

---

## 5. Troubleshooting first-use

| Symptom | Cause | Fix |
|---|---|---|
| App shows **CREATE YOUR ARCHIVE** instead of a password box. | The drive has no vault yet (normal on first use). | Enter a password twice and click CREATE ARCHIVE. This is expected, not an error. |
| `setup-drive.ps1` warns the Windows build is missing. | You have not published yet. | Run `scripts\publish.ps1`, then re-run `setup-drive.ps1`. |
| Script refuses the drive. | It is the system drive, or already has files. | Use a different drive, or add `-Force` (nothing is deleted). |
| *"A .NET SDK matching global.json was not found."* | The SDK is not installed. | Install .NET SDK 10 — see [BUILD.md](BUILD.md). |
