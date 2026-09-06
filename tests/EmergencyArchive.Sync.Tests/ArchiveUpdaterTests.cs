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
}
