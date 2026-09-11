# TODO — Avalonia 11 → 12 migration

**Status:** deferred (not urgent). We are on the stable **Avalonia 11.3.21**
line, which builds and passes the full suite (215 tests) with **zero known
vulnerabilities**. Dependabot is configured to **hold** the Avalonia major
(`.github/dependabot.yml` ignores `Avalonia*` semver-major) so it stops
proposing a PR that cannot build. Do this migration deliberately, on its own
branch, with a full test pass — not as an automated dependency bump.

## Why the automated bump fails today

Avalonia 12 **removed the `Avalonia.Diagnostics` package**
([breaking changes](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)).
Our project references it (Debug-only), so bumping the family to 12.x leaves one
package (`Avalonia.Diagnostics`) with no 12.x version → an unresolvable
version conflict → CI restore failure. You cannot mix Avalonia majors: core
12.x packages require their siblings at `>= 12`.

## Current Avalonia references

- `Directory.Packages.props` — pinned versions (all `11.3.21`):
  `Avalonia`, `Avalonia.Desktop`, `Avalonia.Diagnostics`,
  `Avalonia.Fonts.Inter`, `Avalonia.Themes.Fluent`, `Avalonia.Headless.XUnit`.
- `src/EmergencyArchive.UI/EmergencyArchive.UI.csproj` — references
  `Avalonia`, `Avalonia.Desktop`, `Avalonia.Fonts.Inter`,
  `Avalonia.Themes.Fluent`, and (Debug-only) `Avalonia.Diagnostics`.
- `tests/EmergencyArchive.UI.Tests` — uses `Avalonia.Headless.XUnit`.
- **No code uses `Avalonia.Diagnostics` / `AttachDevTools()`** — the package is
  referenced but never called, so dropping it is safe.

## Migration checklist

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

## Decision to make first

Do we even want the in-app DevTools inspector after 12? If not, this migration
is essentially "drop Diagnostics + bump the family + fix XAML breakage" — a
contained change. If yes, add the new DiagnosticsSupport tooling in step 1.
