# How Emergency Archive Is Secured

This is a plain-language tour of how the app protects your files and passwords.
It is written for someone new to the project. You do not need a security
background to follow it. Each section is short and to the point. Sources and
the tests that back up each claim are listed at the end.

## What This App Is

Emergency Archive turns a USB drive into a locked box for your important
documents and, more recently, your passwords. You set one password. The app
uses that password to scramble (encrypt) everything on the drive so that
without the password the contents are unreadable. It runs straight from the
drive, so there is nothing to install on the computer you plug into.

## The One Rule: Your Password Is the Only Key

Everything rests on your password. The app does not keep a copy of it anywhere,
and there is no reset button and no back door. That is deliberate. It means
nobody, including us, can open your archive without the password. It also means
that if you forget the password, the data is gone for good. Choose a long
passphrase you can remember, such as four or five random words.

Because the password is the only secret, the practical strength of the whole
system comes down to two things: how hard your password is to guess, and how
expensive we make each guess. The next section covers the second part.

## We Do Not Invent Our Own Encryption

A common way security software fails is by rolling its own cryptography and
getting a detail wrong. We avoid that entirely. The document vault uses the
**Cryptomator vault format, version 8**, which is a published, open
specification used by a mature open-source product [1]. The building blocks are
all standard, well-studied algorithms:

- **AES-GCM** to encrypt file contents in small chunks, which also detects
  tampering [4].
- **AES-SIV (RFC 5297)** to encrypt file and folder names [5].
- **AES Key Wrap (RFC 3394)** to protect the internal keys [6].
- **scrypt (RFC 7914)** to turn your password into a key in a way that is slow
  and memory-hungry for attackers [7].
- **HMAC-SHA-256** to sign internal settings so they cannot be altered [8].

The password manager's KeePass import and export uses the documented **KDBX**
format with the same family of standard algorithms, including **Argon2** for
the password step and **ChaCha20** and AES [9][10]. We wrote that code
ourselves from the public format description rather than pulling in a
license-incompatible library, and we pinned it to official test vectors so we
know it matches the real KeePass behavior.

The short version: we implement standard formats on top of trusted libraries
(BouncyCastle and the built-in .NET cryptography), and we test our
implementation against the published reference numbers.

## Making Password Guessing Expensive

If someone steals the drive, they can try to guess the password offline on
their own hardware. We slow that down with scrypt, a "memory-hard" function.
Memory-hard means each guess needs a large chunk of memory, which makes running
millions of guesses on specialized hardware very costly.

We deliberately turn the cost up. Cryptomator's default setting uses about 32
megabytes of memory per attempt. We raise that to about **1 gigabyte** per
attempt for archives the app creates [1]. That does not slow you down in normal
use beyond a short pause when unlocking, but it multiplies an attacker's cost
enormously. On a computer with little spare memory, Setup Mode lets you lower
this.

## Tamper Detection

Encryption keeps data secret. We also want to know if someone changed it. The
format authenticates data as it decrypts, so a flipped bit or a swapped file is
caught rather than silently accepted [1][4]. Internal settings are signed, and
there is a check that stops an attacker from quietly downgrading the archive to
weaker settings. A separate VERIFY feature re-checks every stored document
against its recorded fingerprint and reports anything corrupt or missing.

Be clear about the limit: we can **detect** tampering, but we cannot **prevent**
someone with physical access from deleting or overwriting the drive. The answer
to that is to keep more than one copy. The app helps by tracking versions across
copies of the drive.

## Handling Secrets Carefully in Memory

Good algorithms can still leak through sloppy handling. The code takes several
routine precautions:

- Keys are wiped from memory as soon as they are no longer needed, rather than
  left lying around.
- Security comparisons use constant-time checks, which avoid a subtle trick
  where an attacker measures tiny timing differences to guess a value.
- The password manager keeps secret values scrambled on screen until you choose
  to reveal them, and it never writes them to the activity log. The log records
  that an action happened, not the secret itself.

## Keeping the Rest of the Code Honest

Most real-world breaches do not break the encryption. They slip in through the
edges: a malformed file, a booby-trapped document, a bad file name. We hardened
those edges [2][3]:

- **File names and paths** are checked so a document cannot escape its folder or
  use a reserved Windows device name when you open it.
- **Documents that carry hidden bombs**, such as a compressed file that expands
  to fill your disk, are bounded. Reading stops at a safe limit.
- **Malformed data** in a document or a KeePass file is rejected cleanly with a
  clear error instead of crashing or doing something unsafe. The XML reader used
  for imports has external-entity loading disabled, which shuts down a classic
  document-based attack.
- **Errors are surfaced, not swallowed.** When something goes wrong, it is
  logged and reported rather than hidden.

## Running on a Computer You Do Not Fully Trust

The app is designed to run from the drive without installing anything, which
lets you use it on a borrowed or public computer in an emergency. That
convenience has a hard limit worth stating plainly: **if the computer itself is
compromised, the archive is compromised.** Malware or a keylogger on that
machine can capture your password as you type it, or read a document after you
open it. Opening a document also leaves normal traces on the host, such as
recent-file lists and temporary files. The app warns you when you open a
document. Whenever you can, use a computer you trust.

## What This Does Not Protect Against

Being honest about the gaps is part of being secure. This list is short on
purpose, because these are the things a new user most needs to understand [2].

- **A weak password.** Nothing here saves a guessable password from an offline
  attack. Length and randomness are your defense.
- **A compromised computer.** Covered above. The host must be trusted.
- **Physical destruction.** Someone with the drive can wipe it. Keep copies.
- **Metadata.** The number of files, their rough sizes, and the folder shape are
  visible to someone holding the drive, even though names and contents are not.
- **Unsigned Windows downloads.** Windows may show an "unknown publisher"
  warning until we add a code-signing certificate. This is a trust-on-download
  concern, not a flaw in the encryption.
- **No independent audit yet.** The security work so far is our own careful
  review. It reduces risk and documents our assumptions, but it is not a
  substitute for a paid third-party audit, which is planned before any
  "rely on this completely" release.

## How We Know It Works: Testing

Claims are cheap, so the project backs them with an automated test suite that
runs on every change. As of this writing there are **264 automated tests with
zero failures**, covering:

- **Cryptography conformance.** Our building blocks are checked against the
  official published test numbers: RFC 5297 for AES-SIV [5], RFC 9106 reference
  values for Argon2 [9], and an independent cross-check for ChaCha20 [10]. If
  our code ever drifts from the standard, these tests fail.
- **Round trips.** Encrypt then decrypt, export then re-import, including tricky
  cases like non-English text and emoji, must return exactly what went in.
- **Tamper and wrong-password cases.** A flipped byte, a corrupted header, or a
  wrong password must be rejected, not quietly accepted.
- **Attack-surface cases.** Zip-bomb documents, malformed files, and unsafe file
  names are handled safely.
- **Recovery and power loss.** Pulling the drive mid-update must leave the
  previous good version intact, and a damaged search index must rebuild.

The project also runs a secret scanner and a dependency vulnerability check
before each release, and it currently reports zero known-vulnerable
dependencies [2][3].

## The Honest Bottom Line

Emergency Archive is built on standard, published encryption, turns the cost of
guessing your password up high, detects tampering, handles secrets carefully,
and is tested against the real reference numbers. Its security depends on two
things you control: a strong password and a trustworthy computer to use it on.
It has not yet had an independent audit, so treat it as strong protection for
personal and family use rather than a certified, audited product.

## Sources

1. Cryptography and vault format decision, with the raised scrypt cost and the
   format details as implemented: [docs/CRYPTOGRAPHY.md](CRYPTOGRAPHY.md).
2. Internal adversarial security review and the honest residual-risk statement:
   [docs/SECURITY-REVIEW.md](SECURITY-REVIEW.md).
3. Hardening checklist, static analysis, secret scanning, and dependency audit:
   [docs/HARDENING.md](HARDENING.md).
4. AES-GCM authenticated encryption: NIST Special Publication 800-38D,
   <https://csrc.nist.gov/pubs/sp/800/38/d/final>.
5. AES-SIV deterministic authenticated encryption: RFC 5297,
   <https://www.rfc-editor.org/rfc/rfc5297>.
6. AES Key Wrap: RFC 3394, <https://www.rfc-editor.org/rfc/rfc3394>.
7. scrypt memory-hard key derivation: RFC 7914,
   <https://www.rfc-editor.org/rfc/rfc7914>.
8. HMAC keyed hashing: RFC 2104, <https://www.rfc-editor.org/rfc/rfc2104>.
9. Argon2 password hashing and its test vectors: RFC 9106,
   <https://www.rfc-editor.org/rfc/rfc9106>.
10. ChaCha20 stream cipher and its test vectors: RFC 7539 (updated by RFC 8439),
    <https://www.rfc-editor.org/rfc/rfc7539>.

Content was rephrased for compliance with licensing restrictions.
