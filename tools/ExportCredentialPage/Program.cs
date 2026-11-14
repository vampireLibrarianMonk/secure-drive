using EmergencyArchive.Sync;

// Writes the self-contained HTML export page (CredentialPageBuilder.Build) to a
// file so browser-based test tooling (tools/playwright) can exercise the
// rendered "passwords page" — the project's one HTML surface, kept for the
// export/portability path.
//
// The data is FAKE sample content: two ordinary credentials plus one with a
// deliberately hostile site name, so the page's textContent-based rendering
// (never innerHTML) can be asserted against script-injection attempts.
//
// Usage: dotnet run --project tools/ExportCredentialPage -- <output.html>
//   (default output: password-page.html in the current directory; on hosts
//   without the SDK, run it inside the pinned SDK container — see
//   tools/playwright/README.md)

string output = args.Length > 0 ? args[0] : "password-page.html";

var database = CredentialDatabase.Empty
    .With(new Credential(
        Guid.NewGuid().ToString("N"),
        "bank.example",
        "alice",
        "s3cret-bank",
        [new CredentialField("Recovery code", "RC-42"), new CredentialField("Branch", "Downtown 7")]))
    .With(new Credential(
        Guid.NewGuid().ToString("N"),
        "mail.example",
        "bob",
        "s3cret-mail",
        []))
    .With(new Credential(
        Guid.NewGuid().ToString("N"),
        "<img src=x onerror=window.__pwned=1>", // hostile: must render as text, never execute
        "mallory",
        "s3cret-mallory",
        []));

File.WriteAllText(output, CredentialPageBuilder.Build(database));
Console.WriteLine($"Wrote {Path.GetFullPath(output)}");
