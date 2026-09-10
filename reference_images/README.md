# Reference images

Drop screenshots here to make them appear in the guides. Each image spot in the
docs is marked with an HTML comment describing exactly what to capture, followed
by a Markdown image reference pointing at a file in **this** folder.

## How it works

- The docs reference images by a fixed **slug**, e.g.
  `![...](../reference_images/create-archive-screen.png)`.
- To fill a spot, save your screenshot as a **PNG** with that exact name in this
  folder (for example `create-archive-screen.png`). No doc edits needed — the
  image simply shows up.
- Keep the names exactly as listed below (lowercase, hyphens, `.png`).

## Image checklist

Each entry: **file name** — what to capture. (See the matching
`<!-- IMAGE: ... -->` comment in the doc for full context.)

Used in [docs/USER-GUIDE.md](../docs/USER-GUIDE.md):

- [ ] `hero-main-screen.png` — the unlocked archive: search box + document list grouped by folder (a friendly hero shot).
- [ ] `format-usb-exfat.png` — Windows "Format" dialog for a USB drive, File system set to exFAT.
- [ ] `github-download-release.png` — GitHub Releases page with the latest `.zip` asset highlighted.
- [ ] `drive-contents-after-copy.png` — File Explorer showing the stick after copying: `START-WINDOWS.exe` at top level with `app/ vault/ public/`.
- [ ] `windows-smartscreen-run-anyway.png` — the blue "Windows protected your PC" dialog, showing "More info" → "Run anyway".
- [ ] `create-archive-screen.png` — the CREATE YOUR ARCHIVE screen (new password + repeat + CREATE ARCHIVE button).
- [ ] `setup-overview.png` — the Setup screen showing the numbered cards (1 Estate Planning … 5 Security).
- [ ] `estate-planning-letter.png` — the Estate Planning card with the editable letter box and SAVE LETTER button.
- [ ] `add-documents.png` — the Your Documents card: category dropdown, ADD FILE(S), and folder-import area.
- [ ] `manage-documents.png` — the Manage Documents card with a document selected: rename box, move dropdown, REPLACE FILE / DELETE.
- [ ] `unlock-screen.png` — the password (unlock) screen with the single password box and UNLOCK button.
- [ ] `search-results.png` — search results (e.g. "insurance") with excerpts, grouped by folder.

## Tips for good screenshots

- Use realistic but **fake** sample data — never real passwords, account
  numbers, or personal documents (these images may be committed publicly).
- Crop to the app window; PNG keeps text crisp.
- If you rename a slug, update the matching reference in the doc too.
