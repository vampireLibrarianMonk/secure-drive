using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>Spec section 14: an interrupted update never destroys the last known-good archive.</summary>
public class ArchiveUpdaterSafetyTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;
    private readonly string sourcesDir;

    public ArchiveUpdaterSafetyTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        sourcesDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-sources-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        Directory.CreateDirectory(sourcesDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose()
    {
        Directory.Delete(vaultDir, recursive: true);
        Directory.Delete(sourcesDir, recursive: true);
    }

    [Fact]
    public void Update_IsIdempotent_AfterInterruption()
    {
        // Simulate an interrupted update: content written but manifest NOT committed.
        File.WriteAllText(Path.Combine(sourcesDir, "Home policy.txt"), "The home insurance policy.");
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            SourceConfigStore.Save(session, new SourceConfiguration([new SourceDirectory(sourcesDir)]));
            // No ManifestStore.Save — the commit marker is missing.
        }

        // The next update re-plans from the old (empty) manifest and commits.
        using (VaultSession recoverySession = VaultStore.Unlock(vaultDir, Password))
        {
            ArchiveManifest manifest = ManifestStore.Load(recoverySession)
                ?? ArchiveManifest.Empty("EmergencyArchive", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));
            ArchiveUpdateReport report = ArchiveUpdater.Update(recoverySession, manifest, new SourceConfiguration([new SourceDirectory(sourcesDir)]));

            Assert.Single(report.Plan.Added);
            Assert.NotNull(ManifestStore.Load(recoverySession));
        }

        using VaultSession verifySession = VaultStore.Unlock(vaultDir, Password);
        ArchiveManifest? committed = ManifestStore.Load(verifySession);
        Assert.NotNull(committed);
        Assert.Single(committed.Entries);
    }

    [Fact]
    public void Update_WithConflictingSourceAliases_Throws()
    {
        // Both folders share the leaf name "Data" AND the file name "a.txt"
        // -> same archive path -> conflict.
        string first = Path.Combine(sourcesDir, "One", "Data");
        string second = Path.Combine(sourcesDir, "Two", "Data");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "a.txt"), "a");
        File.WriteAllText(Path.Combine(second, "a.txt"), "b");

        var configuration = new SourceConfiguration(
        [
            new SourceDirectory(first),
            new SourceDirectory(second),
        ]);

        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty("EmergencyArchive", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));

        Assert.Throws<InvalidOperationException>(() => ArchiveUpdater.Update(session, manifest, configuration));
    }
}
