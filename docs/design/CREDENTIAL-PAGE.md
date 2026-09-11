# Design: the searchable credential page

**Idea (owner's proposal):** instead of integrating with browser autofill,
generate a **self-contained, offline HTML page** that looks and behaves like the
app's document search, but for credentials. The user searches by site, sees
website / username / password, and picks an entry from a selection box that
reveals any additional key/value fields for that entry. Secret values are
**obfuscated by default** with an **eye (reveal)** and a **copy** icon. The page
location is surfaced to the user in a friendly way.

This is the recommended answer to "problem #2" (browser interop) from
[PASSWORD-MANAGER-RESEARCH.md](PASSWORD-MANAGER-RESEARCH.md). It supersedes the
"never touch the browser" stance there — **because it is a credential *viewer*,
not an autofill *provider*.** The app produces a plain file; the browser only
displays it; the user copy/pastes. Nothing hooks into browser form-filling, so
there is no per-browser API to rot over time.

## Why this fits the project's principles

- **Universal & long-lived:** a static HTML + inline JS/CSS file with zero
  dependencies opens in any browser now and decades from now. Same longevity
  logic as choosing the Cryptomator vault format.
- **Offline:** `file://`, no network, no server.
- **Consistent UX:** mirrors the document-search screen the user already knows.
- **Copy/reveal, not autofill:** sidesteps the entire moving target of browser
  integration. The user pastes; the page just surfaces the secret.

## UX

- A search box (as-you-type filter) over credential entries.
- Result rows: **website name**, **username**, **password** (obfuscated).
- A selection box per entry; selecting it reveals the **additional fields**
  (arbitrary key → value pairs: recovery codes, PIN, security questions, notes).
- Every secret value: **obfuscated by default**, with an **eye** icon to reveal
  and a **copy** icon to copy. Copy clears the clipboard after a short timeout.
- The app tells the user where the page is, e.g. "Your passwords page is open in
  your browser" (it is launched via the existing OPEN mechanism).

## Security model — the crux

A credential page contains secrets. This is a real departure from the vault
model (where plaintext exists only transiently), so it must be handled like the
existing **OPEN a document** flow, not as a persisted file.

**Non-negotiables:**

1. **Never persisted.** The page is generated **on demand into the per-session
   temp directory** (`EnsureOpenTempDirectory()`), and **shredded on Lock/close**
   by the existing `CleanupTempExports()`. It never lives on the USB drive
   (a plaintext-passwords file on the drive would break "safe if lost") and it
   is not left on the host after locking.
2. **Encrypted-at-rest even in temp (preferred).** Rather than embedding
   plaintext secrets in the HTML, embed the credential blob **encrypted** and
   have the page decrypt it **in the browser via WebCrypto** (Web Crypto API,
   available in every browser since ~2015 — still dependency-free) after the
   user enters a passphrase or a one-time key the app displays. A stray copy of
   the file is then not an instant breach.
3. **No network, provably.** A strict `Content-Security-Policy` meta tag
   (`default-src 'none'`; only an inline hashed script permitted) so the page
   *cannot* make a network request or load anything external — it physically
   cannot exfiltrate, even if a browser extension tried to inject.
4. **No inline event handlers / external refs**; single file only.
5. **Reuse the sanitized OPEN path** (`BuildSafeOpenTargetPath`) so the file is
   written safely inside the temp dir.

**Residual risk (state honestly, same as OPEN today):** while unlocked and the
page is open, a compromised host (malware, another admin user, screen capture)
can read revealed secrets or the temp file. Opening on an untrusted computer is
already discouraged; this inherits that warning. The clipboard, once the user
copies, is outside our control beyond a best-effort timed clear.

## Where the credentials come from

Two options (decide before building):

- **A. Reuse KDBX** (from the research doc): the page is a viewer rendered from a
  `.kdbx` credential file stored in the vault. Keeps the authoritative store in
  a standard, independently-recoverable format; the HTML is a disposable view.
  **Recommended** — the archive stays interoperable.
- **B. Native credential entries** stored as their own encrypted document type in
  the vault, rendered to the page. Simpler to build, but invents a format
  (against our reuse-a-standard rule) unless we still export to KDBX.

Recommendation: **store as KDBX (A), render the page as a disposable view.**

## Phasing

1. **Data model + storage:** credentials as KDBX in the vault (see research doc).
2. **Page generator:** produce the self-contained HTML (CSP-locked, WebCrypto
   decrypt, reveal/copy) into the session temp dir; launch via OPEN; wipe on Lock.
3. **Editing:** manage credential entries in Setup Mode (mirrors document CRUD).
4. **Portability:** CXF import/export bridge (optional, later).

## Open decisions

- Page unlock: **archive password**, a **separate credential password**, or a
  **one-time key** the app shows at generation time? (One-time key is nice: the
  file is useless without the running app that generated it.)
- KDBX-backed (A) vs native entries (B).
- Clipboard auto-clear timeout, and whether to auto-close/expire the page.

## Status

Design only. No code written. This does not change the current build. Requires
sign-off on the security model (esp. #1 and #2) before implementation.
