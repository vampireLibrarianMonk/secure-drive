# Playwright scenarios (credential HTML export page)

The Emergency Archive application itself is a **native Avalonia** app with no
browser surface — it is covered by the repo's own headless UI test suite
(`tests/EmergencyArchive.UI.Tests`, which runs in CI inside the pinned SDK
container). The one thing a browser engine *can* exercise is the project's
single HTML artifact: the **credential export page** rendered by
[`CredentialPageBuilder`](../../src/EmergencyArchive.Sync/CredentialPageBuilder.cs)
(self-contained HTML, strict CSP, `textContent`-only rendering — no
`innerHTML`).

This directory holds a small Playwright suite for exactly that page:

- one card per credential, summary counts
- secret values masked until revealed (and toggling back)
- extra fields hidden behind the toggle, masked as well
- the search box filtering by site/username
- a hostile site name rendered as text — script injection never executes

## One-time setup

```bash
cd tools/playwright
npm install
npx playwright install chromium
```

## Generate the page under test

From a machine with the .NET SDK:

```bash
dotnet run --project ../ExportCredentialPage -- output/password-page.html
```

On a machine without the SDK (the repo's Docker path):

```bash
docker run --rm -v "<repo-root>:/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run --project tools/ExportCredentialPage -- tools/playwright/output/password-page.html
```

The page contains **fake sample data only** (including a deliberately hostile
site name) — no real secrets, so it is safe to keep out of the vault and out of
screenshots.

## Run

```bash
npx playwright test
```

## Notes

- The suite is intentionally **not** part of `dotnet test` / CI: it needs a
  browser download (`npx playwright install`) and adds no coverage the headless
  UI tests lack for the native app.
- `output/` is git-ignored; regenerate the page any time the builder changes.
