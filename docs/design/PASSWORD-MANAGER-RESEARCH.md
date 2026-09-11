# Research: integrating password management

**Question:** can Emergency Archive store passwords/credentials in a way that is
*universal* and *readable for the long term*, given that password managers are
either open-source or commercial and browser autofill interop keeps shifting?

**Short answer:** Yes — store credentials in the **KDBX (KeePass) format** as
just another document inside the vault. Do **not** try to be a live browser
autofill provider. Below is the reasoning, the standards researched, and a
recommended design that stays true to this project's core principle
(*independently recoverable decades from now with maintained open-source
software* — the same reason we chose the Cryptomator vault format).

---

## Separate the two problems

These are usually conflated, but they have very different answers:

1. **Long-term storage** — write credentials down, keep them for decades, open
   them later on any OS. This is a *file-format longevity* problem. **Solvable
   and squarely in scope.**
2. **Live browser autofill** — hand a password to Chrome/Edge/Firefox to fill a
   login form. This is a *live OS/browser integration* problem that changes
   every few years (extensions, native messaging, CXP, platform credential
   providers). **Out of scope, and a longevity trap.**

Our project's promise is offline, no lock-in, still openable in 2050. Only #1
fits that promise. Chasing #2 would tie a long-term archive to short-lived,
platform-specific plumbing — exactly what we avoided by not writing custom
crypto.

## Standards researched

### KDBX (KeePass database) — recommended for storage
- Openly documented, XML-payload-inside-an-encrypted-container format used by
  **KeePass, KeePassXC, KeePassium, KeePassDX, Strongbox** and many more, across
  Windows/macOS/Linux/iOS/Android.
- Multiple **independent open-source implementations** — the same
  "recoverable by many tools" property we rely on for the vault. If our app
  disappears, the credential file still opens in any KeePass-compatible app.
- Self-contained single file → drops straight into our vault as a document.
- Format refs: KeePassXC specs (github.com/keepassxreboot/keepassxc-specs),
  and independent format write-ups (e.g. palant.info KDBX4 documentation).

### FIDO Credential Exchange Format/Protocol (CXF / CXP) — for portability, not storage
- New (2024–2025, working drafts) FIDO Alliance specs (1Password, Bitwarden,
  Dashlane, Google, Apple, etc.) for **moving** credentials — including
  **passkeys** — between providers.
- Value to us: a future **import/export** interchange so a user can bring
  credentials in from, or take them out to, mainstream managers.
- Caveats: still draft-stage and evolving; designed for provider-to-provider
  transfer, not long-term at-rest storage. Treat as a *migration* format, not
  the archive format.

### Passkeys / WebAuthn (FIDO2)
- Passkeys are increasingly the real-world replacement for passwords, but they
  are device/provider-bound public-key credentials — fundamentally a *live
  authentication* mechanism, not a "write it in a book for 30 years" artifact.
- We can *store exported passkey material* (via CXF) as data, but we cannot and
  should not act as a live passkey authenticator. Note this honestly for users.

## Recommendation

**Store credentials as a KDBX file inside the vault; offer CXF import/export as
a portability bridge; never become a live autofill/passkey provider.**

Rationale, in priority order (matches spec §2):
- **Confidentiality** — the KDBX file is itself encrypted, and it also rides
  inside our already-encrypted vault (defense in depth). It is never written to
  the host in plaintext except through the existing, sanitized OPEN/EXPORT path.
- **Long-term recoverability** — KDBX is the credential analogue of our
  Cryptomator choice: many maintained, open tools can open it without us.
- **Simplicity** — to the emergency user it is just another searchable
  document ("Passwords"); no new mental model.
- **Offline / cross-platform** — inherent to a plain file.

### What NOT to do (and why)
- **Do not build a browser extension / native autofill.** It is a permanent
  maintenance treadmill (per-browser APIs shift constantly) and contradicts the
  offline, no-install premise.
- **Do not invent our own credential format.** Same rule as crypto: reuse a
  documented, independently-implemented standard (KDBX).
- **Do not claim to be a passkey authenticator.** Be explicit that passkeys are
  a live mechanism; we can store exported credential data, not perform WebAuthn.

## Possible implementation phases (for a later, deliberate effort)

1. **Read/browse (minimal):** treat a `.kdbx` file in the vault as a first-class
   document — detect it, and let the user OPEN it with their installed KeePass
   app (works today via OPEN; just document it). Zero new crypto.
2. **In-app read:** add a KDBX reader (well-tested open libraries exist for
   .NET, e.g. KeePassLib / KeePassXC-compatible libs — audit license + upkeep)
   so entries are viewable/searchable without a second app. The KDBX password
   can be the archive password or a separate one.
3. **In-app edit/create:** write KDBX entries from within Setup Mode (owner),
   mirroring the document-CRUD we already have.
4. **Portability bridge:** CXF import/export so users can move credentials to/
   from mainstream managers. Gate behind clear warnings (draft standard).

## Open decisions for the team

- Is the KDBX file protected by the **archive password** (one secret, simplest)
  or a **separate password** (compartmentalization, more to remember)?
- Do we ship an in-app KDBX reader (adds a dependency to vet for security +
  longevity), or start by simply letting users OPEN their `.kdbx` with an
  existing KeePass app (zero dependency)?
- Passkeys: store-only via CXF, or explicitly out of scope for now?

## Honest limitations

- KDBX libraries for .NET vary in maintenance and license; any we adopt must be
  audited exactly as carefully as the vault layer, or we implement a minimal
  reader from the documented format.
- CXF/CXP are **working drafts**; building on them now risks churn — treat as
  experimental.
- This is research/design only. No code has been written; nothing here changes
  the current build.
