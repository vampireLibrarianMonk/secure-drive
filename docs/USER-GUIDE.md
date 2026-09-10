# Emergency Archive — User Guide

This is the friendly, no-jargon guide to setting up and using your Emergency
Archive. You do **not** need to know anything about programming.

**What this is:** an app that runs from a USB stick and keeps your important
documents (IDs, insurance, wills, medical, financial) locked with one password,
so you — or your family in an emergency — can search and open them on any
Windows computer. No internet, no accounts, nothing installed on the computer.

<!-- IMAGE: The unlocked archive showing the search box and a list of documents grouped by folder. A friendly "hero" shot of the main screen. -->
![The Emergency Archive main screen](../reference_images/hero-main-screen.png)

> **Two kinds of people use this guide:**
> - **The owner** sets the archive up and adds documents (Parts 1–4).
> - **Anyone in an emergency** just needs to open it and find a document
>   (jump to [Part 5](#part-5--find-a-document-emergency-use)).

---

## Part 1 — Get a USB stick ready

- Almost any USB stick of **8 GB or larger** works. A 32 GB stick is ideal.
- The stick should be formatted as **exFAT** (this is the most common format and
  works on both Windows and Mac). Most new sticks already are.

To check or set the format on Windows: open **This PC**, right-click the USB
drive, choose **Format…**, set **File system** to **exFAT**, and click Start.
Formatting **erases the stick**, so use an empty one or copy anything off first.

<!-- IMAGE: Windows "Format" dialog for a USB drive with the File system dropdown set to exFAT. -->
![Formatting a USB stick as exFAT on Windows](../reference_images/format-usb-exfat.png)

> Tip: note the **drive letter** Windows gives the stick (for example `D:` or
> `E:`). You will use it in the next step.

---

## Part 2 — Download the app

1. Go to the project's **Releases** page on GitHub.
2. Under the latest release, download the package for your computer:
   - **Windows:** `EmergencyArchive-<version>-windows.zip`
   - **Linux:** `EmergencyArchive-<version>-linux.zip`

<!-- IMAGE: GitHub Releases page with the latest release's downloadable .zip assets (windows and linux) highlighted. -->
![Downloading the latest release from GitHub](../reference_images/github-download-release.png)

> If you are not sure where the Releases page is, look on the project's main
> page for a link that says **Releases** on the right-hand side. Nothing gets
> installed on the computer — the whole app lives in this ZIP.

---

## Part 3 — Put the app on the stick

1. Open the `.zip` you downloaded (double-click it).
2. Copy everything inside it onto the **top level** of your USB stick — so the
   stick ends up with a file named **`START-WINDOWS.exe`** and folders like
   `app`, `vault`, and `public`.

<!-- IMAGE: File Explorer showing the USB drive contents after copying: START-WINDOWS.exe at the top level alongside app/ vault/ public/ folders. -->
![The USB stick after copying the app onto it](../reference_images/drive-contents-after-copy.png)

That is the whole "install." Nothing goes onto the computer itself — everything
lives on the stick.

---

## Part 4 — First launch: create your password

1. Open the USB stick in File Explorer and **double-click `START-WINDOWS.exe`**.

   <!-- IMAGE: Windows SmartScreen "Windows protected your PC" dialog, with the "More info" link and then the "Run anyway" button indicated. -->
   ![Getting past the Windows "unknown publisher" warning](../reference_images/windows-smartscreen-run-anyway.png)

   > **If Windows shows a blue "Windows protected your PC" warning:** this is
   > normal for an app downloaded from the internet. Click **More info**, then
   > **Run anyway**. (The app is safe; Windows simply doesn't recognise the
   > publisher.)

2. Because the stick has no archive yet, you'll see the **CREATE YOUR ARCHIVE**
   screen. Type a password twice and click **CREATE ARCHIVE**.

   <!-- IMAGE: The CREATE YOUR ARCHIVE screen with the new-password and repeat-password boxes and the CREATE ARCHIVE button. -->
   ![The Create Your Archive screen](../reference_images/create-archive-screen.png)

> **Choosing a good password (this matters):**
> - Use at least **12 characters**. Four or five random words like
>   `river-copper-morning-lantern` are strong and easy to remember.
> - **There is no password reset.** If the password is lost, the documents
>   cannot be recovered. Store it somewhere safe (a password manager, or paper
>   in a secure place) and never keep it written on or with the stick.

---

## Part 5 — Add your documents and write a family letter

After creating the archive, the app opens in **Setup**. (You can always get
back here later by unlocking and clicking **SETUP**.)

<!-- IMAGE: The Setup screen showing the numbered cards: 1 Estate Planning, 2 Your Documents, 3 Manage Documents, 4 Maintenance, 5 Security. -->
![The Setup screen with its numbered cards](../reference_images/setup-overview.png)

### 1. Write a letter for your family (Estate Planning)

The first card is a plain-language **letter** for whoever opens this stick in an
emergency — how to search, where things are, and who to call. A starter letter
is already filled in; edit it however you like, then click **SAVE LETTER**.

<!-- IMAGE: The Estate Planning card with the editable letter text box and the SAVE LETTER button. -->
![Editing the family letter](../reference_images/estate-planning-letter.png)

### 2. Add your documents

In the **Your documents** card you have two easy options:

- **Add individual files** — pick a category (Identity, Financial, Insurance,
  Property, Legal, Medical, Family), click **ADD FILE(S)…**, and choose one or
  more files. They are added right away.
- **Import whole folders** — click **ADD FOLDER…** to point at a folder on your
  computer, then **IMPORT / UPDATE ARCHIVE** to bring everything in.

<!-- IMAGE: The Your Documents card showing the category dropdown, ADD FILE(S) button, and the folder-import area. -->
![Adding documents to the archive](../reference_images/add-documents.png)

Your documents are searchable the moment you add them.

### 3. Change or manage documents later (optional)

The **Manage documents** card lists everything in the archive. Select a document
to **rename** it, **move** it to another category, **replace** it with a newer
file, or **delete** it.

<!-- IMAGE: The Manage Documents card with a document selected, showing the rename box, move dropdown, and REPLACE FILE / DELETE buttons. -->
![Managing existing documents](../reference_images/manage-documents.png)

### When you're done

Click **LOCK**, or just close the window. The archive locks immediately and the
password is forgotten. Always lock before unplugging the stick.

---

## Part 6 — Find a document (emergency use)

This is all someone needs to know in an emergency:

1. **Plug the stick** into a Windows computer — ideally one you trust.
2. Open the stick and **double-click `START-WINDOWS.exe`**.
3. **Type the password** and click **UNLOCK**.

   <!-- IMAGE: The password (unlock) screen with the single password box and UNLOCK button. -->
   ![The unlock screen](../reference_images/unlock-screen.png)

4. **Search.** Two ways:
   - **Type** to filter the list by file **name** as you go.
   - Press **ENTER** (or **SEARCH**) to also search **inside** the documents —
     so `home insurance` finds the policy even if those words are only in the
     document text. Results show a short excerpt and are grouped by folder.

   <!-- IMAGE: Search results for a query like "insurance" showing matching documents with text excerpts, grouped by folder. -->
   ![Searching inside the documents](../reference_images/search-results.png)

   > Content search works for PDF, text, Word, Excel, PowerPoint, and web pages.
   > Scanned pictures are found by their **name** only.

5. **Open or save a copy.** Select a document, then **OPEN** to view it, or
   **EXPORT / COPY** to save an unencrypted copy somewhere (for example, to
   print it).
6. **Lock when finished** (button or close the window).

### The 5-second pause is normal

After **every** password try, the app makes you wait **5 seconds** before the
next try, with a countdown. This is a deliberate protection against someone
guessing passwords. Enter the password carefully and wait for the countdown.

### A few emergency rules

- Opening the archive is **read-only** — you can't damage anything by looking.
- On a computer you don't trust, keep it brief: opening a document can leave
  traces on that computer.
- **Don't rename, move, or delete files on the stick** from File Explorer, and
  remove the stick only **after locking**.

---

## Troubleshooting

| What you see | What it means | What to do |
|---|---|---|
| Blue "Windows protected your PC" warning | Windows doesn't recognise the app's publisher (normal for internet downloads). | Click **More info** → **Run anyway**. |
| The app opens to **CREATE YOUR ARCHIVE** | The stick has no archive yet (first use). | Set a password and click CREATE ARCHIVE. This is expected. |
| *Unable to unlock archive. Check the password…* | Wrong password. | Try again carefully after the 5-second wait. There is **no** password recovery. |
| *Archive integrity problem detected. Do not modify this USB.* | The archive on the stick may be damaged. | Don't write to the stick. Use another copy of the stick if you have one, or see `public\RECOVERY-INSTRUCTIONS.txt` on the drive. |
| A search finds nothing you expected | The search index may need rebuilding. | In **Setup → Maintenance**, click **REBUILD SEARCH INDEX**. Your documents are safe. |

---

## A note for the future

Even if this app someday stops working, your documents are **not** locked to it.
They're stored in a well-known open-source encrypted format (Cryptomator), and
the file `public\RECOVERY-INSTRUCTIONS.txt` on the stick explains how a
tech-savvy helper can open them with free software, using only your password.

---

*Setting a drive up from the command line, or resetting one to start over?*
See [FIRST-USE.md](FIRST-USE.md). *Building the code?* See the
[README](../README.md).
