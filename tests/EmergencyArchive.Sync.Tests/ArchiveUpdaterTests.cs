using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>End-to-end archive update tests against a real encrypted vault (spec sections 13-14).</summary>
public class ArchiveUpdaterTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;
    private readonly string sourcesDir;

    public ArchiveUpdaterTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        sourcesDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-sources-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        Directory.CreateDirectory(sourcesDir);
        Directory.CreateDirectory(Path.Combine(sourcesDir, "Subfolder"));
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose()
    {
        Directory.Delete(vaultDir, recursive: true);
        Directory.Delete(sourcesDir, recursive: true);
    }

    internal SourceConfiguration Config() => new([new SourceDirectory(sourcesDir, Alias: "sources")]);

    internal void WriteSourceFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(sourcesDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    internal ArchiveManifest InitialUpdate()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        SourceConfigStore.Save(session, Config());
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty("EmergencyArchive", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));
        ArchiveUpdater.Update(session, manifest, Config());
        return ManifestStore.Load(session)!;
    }

    [Fact]
    public void FirstUpdate_AddsAllDocuments_WritesManifest_AndCommitsVersion()
    {
        WriteSourceFile("Home policy.txt", "The home insurance policy.");
        WriteSourceFile("Subfolder/Passport scan.txt", "passport");
        WriteSourceFile("skipme.tmp", "temporary junk"); // excluded by default

        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        SourceConfigStore.Save(session, Config());
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty("EmergencyArchive", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));

        ArchiveUpdateReport report = ArchiveUpdater.Update(session, manifest, Config());

        Assert.Equal(2, report.Plan.Added.Count);
        Assert.Equal(2, report.DocumentCount);
        Assert.Matches(@"^\d{4}\.\d{2}\.\d{2}\.001$", report.ArchiveVersion);
        Assert.Equal(2, ManifestStore.Load(session)!.DocumentCount);
    }

    [Fact]
    public void SecondUpdate_DetectsChanges_AndUpdatesIndexIncrementally()
    {
        WriteSourceFile("Home policy.txt", "The home insurance policy.");
        WriteSourceFile("Subfolder/Passport scan.txt", "passport");
        InitialUpdate();

        // Change one file, add another, delete a third.
        WriteSourceFile("Home policy.txt", "The UPDATED home insurance policy with boat coverage.");
        WriteSourceFile("New bank details.txt", "IBAN DE89 3704 0044");
        File.Delete(Path.Combine(sourcesDir, "Subfolder", "Passport scan.txt"));

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            ArchiveManifest manifest = ManifestStore.Load(session)!;
            ArchiveUpdateReport report = ArchiveUpdater.Update(session, manifest, Config());

            Assert.Single(report.Plan.Added);
            Assert.Single(report.Plan.Changed);
            Assert.Single(report.Plan.Deleted);
            Assert.Matches(@"\.002$", report.ArchiveVersion);
        }

        // The index reflects the changes (incremental index updates).
        using VaultSession verifySession = VaultStore.Unlock(vaultDir, Password);
        bool indexFileExists = verifySession.FileExists(VaultSearchIndex.IndexPath);
        var vaultFiles = verifySession.EnumerateFiles().ToList();
        using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(verifySession);
        Assert.True(
            index.DocumentCount == 2,
            $"indexFileExists={indexFileExists}, status={index.Status}, count={index.DocumentCount}, files=[{string.Join(" | ", vaultFiles)}]");
        Assert.Contains(index.Search("boat coverage"), r => r.RelativePath == "sources/Home policy.txt");
        Assert.DoesNotContain(index.Search("passport"), r => r.RelativePath == "sources/Subfolder/Passport scan.txt");
        Assert.Contains(index.Search("IBAN"), r => r.RelativePath == "sources/New bank details.txt");
    }

    [Fact]
    public void Update_WithNoChanges_IsANoOp()
    {
        WriteSourceFile("Home policy.txt", "content");
        InitialUpdate();

        using VaultSession secondSession = VaultStore.Unlock(vaultDir, Password);
        ArchiveManifest current = ManifestStore.Load(secondSession)!;
        ArchiveUpdateReport report = ArchiveUpdater.Update(secondSession, current, Config());

        Assert.True(report.NoChanges);
        Assert.Equal(current.ArchiveVersion, report.ArchiveVersion);
    }

    [Fact]
    public void ManuallyAddedDocument_SurvivesFolderUpdate_AndIsNeverDeleted()
    {
        // A folder-sourced document exists...
        WriteSourceFile("Home policy.txt", "content");
        InitialUpdate();

        const string manualPath = "Legal/will.txt";

        // ...and the owner adds an individual document (ManifestSource.Manual)
        // that is NOT in any source folder.
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.WriteFile(manualPath, System.Text.Encoding.UTF8.GetBytes("last will and testament"));
            ArchiveManifest manifest = ManifestStore.Load(session)!;
            var entries = new List<ManifestEntry>(manifest.Entries)
            {
                new ManifestEntry(manualPath, 23, DateTimeOffset.UtcNow, new string('a', 64))
                {
                    Source = ManifestSource.Manual,
                },
            };
            ManifestStore.Save(session, manifest with { Entries = entries });
        }

        // A later folder UPDATE (manual doc absent from the source scan) must
        // NOT delete it and must keep it in the committed manifest.
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            WriteSourceFile("Home policy.txt", "changed content so the update is not a no-op");
            ArchiveManifest manifest = ManifestStore.Load(session)!;
            ArchiveUpdateReport report = ArchiveUpdater.Update(session, manifest, Config());

            Assert.DoesNotContain(manualPath, report.Plan.Deleted);
        }

        // The manual document is still present, still Manual, still readable.
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Assert.True(session.FileExists(manualPath));
            ArchiveManifest manifest = ManifestStore.Load(session)!;
            ManifestEntry? entry = manifest.Find(manualPath);
            Assert.NotNull(entry);
            Assert.Equal(ManifestSource.Manual, entry!.Source);
            Assert.Equal("last will and testament", System.Text.Encoding.UTF8.GetString(session.ReadFile(manualPath)));
        }
    }

    [Fact]
    public void DeletingADocument_RemovesItFromVault_Manifest_AndIndex()
    {
        WriteSourceFile("Home policy.txt", "the home insurance policy");
        WriteSourceFile("New bank details.txt", "IBAN DE89 3704 0044");
        InitialUpdate();

        const string toDelete = "sources/New bank details.txt";

        // Delete using the same primitives the Setup "Delete" action uses:
        // RemoveFile + manifest entry removal + index delete-plan.
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Assert.True(session.RemoveFile(toDelete));

            ArchiveManifest manifest = ManifestStore.Load(session)!;
            var entries = manifest.Entries
                .Where(e => !string.Equals(e.RelativePath, toDelete, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ArchiveManifest updated = manifest with { Entries = entries };
            ManifestStore.Save(session, updated);

            using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
            index.ApplyChanges(session, new SyncPlan([], [], [toDelete]), updated);
            index.Save(session);
        }

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Assert.False(session.FileExists(toDelete));
            Assert.Null(ManifestStore.Load(session)!.Find(toDelete));
            Assert.Equal(1, ManifestStore.Load(session)!.DocumentCount);

            using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
            Assert.DoesNotContain(index.Search("IBAN"), r => r.RelativePath == toDelete);
            Assert.Contains(index.Search("insurance"), r => r.RelativePath == "sources/Home policy.txt");
        }
    }

    // The rename/move edit operations reduce to this relocate primitive:
    // write-new + remove-old + manifest (preserving Source) + index re-point.
    private static void Relocate(VaultSession session, string from, string to)
    {
        byte[] content = session.ReadFile(from);
        session.WriteFile(to, content);
        session.RemoveFile(from);

        ArchiveManifest manifest = ManifestStore.Load(session)!;
        ManifestSource source = manifest.Find(from)?.Source ?? ManifestSource.Manual;
        var entries = manifest.Entries
            .Where(e => !string.Equals(e.RelativePath, from, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(e.RelativePath, to, StringComparison.OrdinalIgnoreCase))
            .ToList();
        entries.Add(new ManifestEntry(to, content.Length, DateTimeOffset.UtcNow,
            EmergencyArchive.Integrity.Sha256.ComputeHash(new MemoryStream(content)))
        {
            Source = source,
        });
        ArchiveManifest updated = manifest with { Entries = entries };
        ManifestStore.Save(session, updated);

        using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
        index.ApplyChanges(session, new SyncPlan([to], [], [from]), updated);
        index.Save(session);
    }

    [Fact]
    public void RenamingADocument_MovesVaultManifestAndIndex_AndPreservesProvenance()
    {
        WriteSourceFile("Home policy.txt", "the home insurance policy");
        InitialUpdate();

        const string from = "sources/Home policy.txt";
        const string to = "sources/Home insurance 2026.txt";

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Relocate(session, from, to);
        }

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Assert.False(session.FileExists(from));
            Assert.True(session.FileExists(to));

            ArchiveManifest manifest = ManifestStore.Load(session)!;
            Assert.Null(manifest.Find(from));
            Assert.NotNull(manifest.Find(to));
            Assert.Equal(ManifestSource.Folder, manifest.Find(to)!.Source); // provenance kept
            Assert.Equal(1, manifest.DocumentCount);

            using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
            Assert.Contains(index.Search("insurance"), r => r.RelativePath == to);
            Assert.DoesNotContain(index.Search("insurance"), r => r.RelativePath == from);
        }
    }

    [Fact]
    public void MovingADocumentToAnotherCategory_UpdatesCategory_AndKeepsContent()
    {
        WriteSourceFile("will.txt", "last will and testament");
        InitialUpdate();

        const string from = "sources/will.txt";
        const string to = "Legal/will.txt";

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Relocate(session, from, to);
        }

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Assert.False(session.FileExists(from));
            Assert.Equal("last will and testament", System.Text.Encoding.UTF8.GetString(session.ReadFile(to)));

            using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
            SearchResultItem hit = index.Search("testament").Single(r => r.RelativePath == to);
            Assert.Equal("Legal", hit.Category);
        }
    }

    [Fact]
    public void ReplacingContent_RehashesManifest_AndReindexesNewText()
    {
        WriteSourceFile("notes.txt", "old contents about apples");
        InitialUpdate();

        const string path = "sources/notes.txt";

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            byte[] newContent = System.Text.Encoding.UTF8.GetBytes("new contents about oranges");
            session.WriteFile(path, newContent); // overwrite in place

            ArchiveManifest manifest = ManifestStore.Load(session)!;
            var entries = manifest.Entries.Where(e => e.RelativePath != path).ToList();
            entries.Add(new ManifestEntry(path, newContent.Length, DateTimeOffset.UtcNow,
                EmergencyArchive.Integrity.Sha256.ComputeHash(new MemoryStream(newContent))));
            ArchiveManifest updated = manifest with { Entries = entries };
            ManifestStore.Save(session, updated);

            using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
            index.ApplyChanges(session, new SyncPlan([], [path], []), updated);
            index.Save(session);
        }

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Assert.Equal("new contents about oranges", System.Text.Encoding.UTF8.GetString(session.ReadFile(path)));
            using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
            Assert.Contains(index.Search("oranges"), r => r.RelativePath == path);
            Assert.DoesNotContain(index.Search("apples"), r => r.RelativePath == path);
        }
    }
}
