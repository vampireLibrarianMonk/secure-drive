# TODO

## Avalonia 11 → 12 migration

**Status:** deferred (not urgent). We are on the stable **Avalonia 11.3.21**
line, which builds and passes the full suite (316 tests) with **zero known
vulnerabilities**. Dependabot is configured to **hold** the Avalonia major
(`.github/dependabot.yml` ignores `Avalonia*` semver-major) so it stops
proposing a PR that cannot build. Do this migration deliberately, on its own
branch, with a full test pass — not as an automated dependency bump.

### Why the automated bump fails today

Avalonia 12 **removed the `Avalonia.Diagnostics` package**
([breaking changes](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)).
Our project references it (Debug-only), so bumping the family to 12.x leaves one
package (`Avalonia.Diagnostics`) with no 12.x version → an unresolvable
version conflict → CI restore failure. You cannot mix Avalonia majors: core
12.x packages require their siblings at `>= 12`.

### Current Avalonia references

- `Directory.Packages.props` — pinned versions (all `11.3.21`):
  `Avalonia`, `Avalonia.Desktop`, `Avalonia.Diagnostics`,
  `Avalonia.Fonts.Inter`, `Avalonia.Themes.Fluent`, `Avalonia.Headless.XUnit`.
- `src/EmergencyArchive.UI/EmergencyArchive.UI.csproj` — references
  `Avalonia`, `Avalonia.Desktop`, `Avalonia.Fonts.Inter`,
  `Avalonia.Themes.Fluent`, and (Debug-only) `Avalonia.Diagnostics`.
- `tests/EmergencyArchive.UI.Tests` — uses `Avalonia.Headless.XUnit`.
- **No code uses `Avalonia.Diagnostics` / `AttachDevTools()`** — the package is
  referenced but never called, so dropping it is safe.

### Migration checklist

### 1. Remove the discontinued Diagnostics package
- [ ] Delete the Debug-only `<PackageReference Include="Avalonia.Diagnostics" />`
      from `src/EmergencyArchive.UI/EmergencyArchive.UI.csproj`.
- [ ] Remove `Avalonia.Diagnostics` from `Directory.Packages.props`.
- [ ] (Optional dev tooling) Evaluate Avalonia's replacement DevTools
      (`AvaloniaUI.DiagnosticsSupport` / the new Developer Tools). Only add it
      back if the team wants the in-app inspector; it is not required to ship.

### 2. Bump the Avalonia family to 12.x (as one set)
- [ ] In `Directory.Packages.props`, move all remaining `Avalonia*` packages to
      the same 12.x version (e.g. the latest 12.1.x): `Avalonia`,
      `Avalonia.Desktop`, `Avalonia.Fonts.Inter`, `Avalonia.Themes.Fluent`,
      `Avalonia.Headless.XUnit`.
- [ ] Confirm `Avalonia.Headless.XUnit` has a matching 12.x (the headless UI
      tests depend on it; if it lags, the UI test project blocks the upgrade).

### 3. Fix Avalonia 12 breaking changes
- [ ] Read the full
      [Avalonia 12 breaking changes](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)
      and address each that applies.
- [ ] Re-check our XAML in `MainWindow.axaml` and `Styles/AppStyles.axaml`
      (control templates, `Classes` selectors, Fluent theme brushes/resources)
      against v12 — theme resource keys and selector syntax are common breakage
      points.
- [ ] Rebuild the UI project; resolve any XAML compile / binding errors.

### 4. Verify
- [ ] `dotnet build EmergencyArchive.slnx` clean (warnings-as-errors is on).
- [ ] Full suite green: `scripts/build-in-docker.ps1 -Action test`
      (headless UI tests exercise the real window tree under v12).
- [ ] `dotnet format --verify-no-changes` clean.
- [ ] Manually launch the app and click through unlock → search → setup cards →
      open/export → lock (headless tests cannot catch every visual regression).
- [ ] `dotnet list package --vulnerable --include-transitive` still clean.

### 5. Ship
- [ ] Update `CHANGELOG.md` (`[Unreleased]` → note the Avalonia 12 upgrade).
- [ ] Re-enable the Avalonia major in `.github/dependabot.yml` (remove the
      `ignore` block) once we are on 12, so future 12.x updates flow normally.
- [ ] Bump `VERSION` + `Directory.Build.props`, tag a release, redeploy to the
      drive, and re-publish packages.

### Decision to make first

Do we even want the in-app DevTools inspector after 12? If not, this migration
is essentially "drop Diagnostics + bump the family + fix XAML breakage" — a
contained change. If yes, add the new DiagnosticsSupport tooling in step 1.

---

## Release trust / anti-tamper options (researched 2026-11-14)

Goal: people who download a release from GitHub can tell it is unadulterated,
and the SmartScreen/Defender friction around our unsigned builds shrinks.
Context: `scripts/assemble-release.ps1` already ships `SHA256SUMS.txt` +
provenance notes in the release; the options below build on that.

### Option 1 — GitHub artifact attestations (free; recommended first step)

- [x] Add `actions/attest-build-provenance` to `.github/workflows/release.yml`
      for the two `.zip` assets. *(landed as `@v1` in the round-6 supply-chain
      work, with `id-token: write` + `attestations: write` permissions)*
- [x] Document verification in the README + release notes:
      `gh attestation verify EmergencyArchive-<ver>-windows.zip -R vampireLibrarianMonk/secure-drive`
      *(README "Verify your download" section added in the v0.5.0 release
      commit; `docs/HARDENING.md` already listed it)*
- Sigstore-based; binds each artifact to the exact workflow run + source
  commit of this repo. Free for public repos. This is the strongest
  machine-checkable "unadulterated" proof — unlike a checksum, a trojaned
  mirror cannot forge it.

### Option 2 — In-app "Verified intact" banner via signed release manifest

- [ ] `scripts/assemble-release.ps1`: hash every released file
      (`START-WINDOWS.exe`, `app/`, …) into a manifest; sign it (Ed25519)
      with a key held as a GitHub Actions secret; ship `manifest.signed`;
      embed the public key in the launcher binary.
- [ ] Launcher startup: verify every file against the manifest **before**
      unlocking. Green "✓ Verified intact — build <sha>" ribbon on success;
      red "Modified — re-download" warning and no vault UI on mismatch.
- Catches casual tampering of the stick — the realistic threat for family
  users. Known limit: an attacker who can rewrite the payload can also strip
  the check, so this is a visible layer **on top of** Option 1, never a
  replacement. No third party involved; reuses the existing assemble
  pipeline.
- **Veracity boundary (analyzed 2026-11-14): embedded hashes alone prove
  integrity, not authenticity.** A program cannot meaningfully verify itself
  against a hash embedded in itself — whoever modifies the code also updates
  the embedded hash. Verifying the payload files (`app/`) against a manifest
  embedded in the launcher is stronger (catches payload swaps and accidental
  corruption), and *signing* the manifest raises the bar to "attacker needs
  our private key" — but an attacker with write access to the stick can swap
  the launcher's embedded public key too. The rule: **an on-load check is
  only as trustworthy as the trust anchor it uses, and anything stored on the
  stick can be replaced together with the app.** Real anchors live elsewhere:
  (a) the user at download time, comparing the published SHA256 on the HTTPS
  GitHub release page / `gh attestation verify` — this is the version of a
  "published hash check" that has full veracity; (b) the OS verifying an
  Authenticode signature (Option 3); (c) GitHub's transparency log
  (Option 1). A launcher that *fetches* the published SHA256SUMS over HTTPS
  at load is genuinely meaningful while online (a tampered stick cannot forge
  github.com's TLS), but in this app it must be advisory-only — the stick is
  for offline emergencies, and offline you cannot distinguish "GitHub
  unreachable" from "deliberately blocked".

### Option 3 — Code signing (DECIDED: declined for now, 2026-11-14)

- **Not worth ~$9.99/mo (Azure Artifact Signing, individual devs) for an app
  used by two people.** Revisit only if the user base grows.
- Free/cheap OSS routes if that decision changes: SignPath Foundation
  (free for accepted OSS projects, signpath.org) or Certum Open Source cloud
  signing (~tens of €/yr, shop.certum.eu).
- Signing is the only mechanism that makes the SmartScreen "unknown publisher"
  dialog fade for end users (reputation builds up across installs).
- Until then, if a **real** Defender flag ever appears on a user machine:
  Windows Security → Protection history → "Allow on device", and report the
  false positive at https://www.microsoft.com/en-us/wdsi/filesubmission.
  Note (2026-11-14): Defender has **zero** detections for our exe — the
  1–2 minute launch stall observed on the dev machine is the SmartScreen
  reputation check timing out and failing open, not a detection.
