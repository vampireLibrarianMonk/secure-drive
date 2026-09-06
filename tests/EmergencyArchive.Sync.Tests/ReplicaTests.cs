using System.Text;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>Replica inspection and comparison (spec section 17).</summary>
public class ReplicaTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string tempRoot;
    private readonly string vaultADir;
    private readonly string vaultBDir;
    private readonly string sourcesADir;
    private readonly string sourcesBDir;

    public ReplicaTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        vaultADir = Path.Combine(tempRoot, "vaultA");
        vaultBDir = Path.Combine(tempRoot, "vaultB");
        sourcesADir = Path.Combine(tempRoot, "sourcesA");
        sourcesBDir = Path.Combine(tempRoot, "sourcesB");
        Directory.CreateDirectory(vaultADir);
        Directory.CreateDirectory(vaultBDir);
        Directory.CreateDirectory(sourcesADir);
        Directory.CreateDirectory(sourcesBDir);
        VaultStore.Create(vaultADir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
        VaultStore.Create(vaultBDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose() => Directory.Delete(tempRoot, recursive: true);

    internal static void UpdateVault(string vaultDir, string sourcesDir)
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        SourceConfigStore.Save(session, new SourceConfiguration([new SourceDirectory(sourcesDir, Alias: "docs")]));
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty("EmergencyArchive", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));
        ArchiveUpdater.Update(session, manifest, new SourceConfiguration([new SourceDirectory(sourcesDir, Alias: "docs")]));
    }

    [Fact]
    public void Inspect_UncommittedVault_ReportsNeverCommitted()
    {
        using VaultSession session = VaultStore.Unlock(vaultADir, Password);

        ReplicaInfo info = ReplicaInspector.Inspect(session, vaultADir);

        Assert.False(info.ManifestPresent);
        Assert.False(info.IsCommitted);
        Assert.Equal(0, info.Revision);
        Assert.Equal(0, info.DocumentCount);
    }

    [Fact]
    public void Inspect_AfterUpdate_ReportsCommittedState()
    {
        File.WriteAllText(Path.Combine(sourcesADir, "Birth Certificate.pdf"), "registry data");
        UpdateVault(vaultADir, sourcesADir);

        using VaultSession session = VaultStore.Unlock(vaultADir, Password);
        ReplicaInfo info = ReplicaInspector.Inspect(session, vaultADir);

        Assert.True(info.ManifestPresent);
        Assert.True(info.IsCommitted);
        Assert.Equal(1, info.Revision);
    }

    [Fact]
    public void Compare_InSync_WhenSameDocumentsCommitted()
    {
        File.WriteAllText(Path.Combine(sourcesADir, "a.txt"), "same content");
        File.WriteAllText(Path.Combine(sourcesBDir, "a.txt"), "same content");
        UpdateVault(vaultADir, sourcesADir);
        UpdateVault(vaultBDir, sourcesBDir);

        using VaultSession sessionA = VaultStore.Unlock(vaultADir, Password);
        using VaultSession sessionB = VaultStore.Unlock(vaultBDir, Password);
        ReplicaComparison comparison = ReplicaComparer.Compare(
            ReplicaInspector.Inspect(sessionA, vaultADir),
            ReplicaInspector.Inspect(sessionB, vaultBDir));

        Assert.Equal(ReplicaSyncState.InSync, comparison.State);
    }

    [Fact]
    public void Compare_DetectsDivergence_AtSameRevision()
    {
        // Spec section 17: same version, different content = diverged.
        File.WriteAllText(Path.Combine(sourcesADir, "a.txt"), "replica A content");
        UpdateVault(vaultADir, sourcesADir);
        File.WriteAllText(Path.Combine(sourcesBDir, "a.txt"), "replica B content");
        UpdateVault(vaultBDir, sourcesBDir);

        using VaultSession sessionA = VaultStore.Unlock(vaultADir, Password);
        using VaultSession sessionB = VaultStore.Unlock(vaultBDir, Password);
        ReplicaComparison comparison = ReplicaComparer.Compare(
            ReplicaInspector.Inspect(sessionA, vaultADir),
            ReplicaInspector.Inspect(sessionB, vaultBDir));

        Assert.Equal(ReplicaSyncState.Diverged, comparison.State);
    }

    [Fact]
    public void Compare_DetectsOlderReplica()
    {
        File.WriteAllText(Path.Combine(sourcesADir, "a.txt"), "v1");
        UpdateVault(vaultADir, sourcesADir);
        File.WriteAllText(Path.Combine(sourcesADir, "a.txt"), "v2");
        UpdateVault(vaultADir, sourcesADir); // A at revision 2

        File.WriteAllText(Path.Combine(sourcesBDir, "a.txt"), "v1");
        UpdateVault(vaultBDir, sourcesBDir); // B at revision 1

        using VaultSession sessionA = VaultStore.Unlock(vaultADir, Password);
        using VaultSession sessionB = VaultStore.Unlock(vaultBDir, Password);
        ReplicaComparison comparison = ReplicaComparer.Compare(
            ReplicaInspector.Inspect(sessionA, vaultADir),
            ReplicaInspector.Inspect(sessionB, vaultBDir));

        Assert.Equal(ReplicaSyncState.FirstIsNewer, comparison.State);
    }

    [Fact]
    public void Compare_DifferentArchives_IsDetected()
    {
        // Two vaults updated from different sources with different archive IDs
        // (spec section 17: different archive IDs = different archives).
        File.WriteAllText(Path.Combine(sourcesADir, "a.txt"), "archive one content");
        File.WriteAllText(Path.Combine(sourcesBDir, "a.txt"), "archive two content");

        using (VaultSession session = VaultStore.Unlock(vaultADir, Password))
        {
            SourceConfigStore.Save(session, new SourceConfiguration([new SourceDirectory(sourcesADir)]));
            ArchiveManifest manifest = ManifestStore.Load(session)
                ?? ArchiveManifest.Empty("Archive-One", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));
            ArchiveUpdater.Update(session, manifest, new SourceConfiguration([new SourceDirectory(sourcesADir)]));
        }

        using (VaultSession session = VaultStore.Unlock(vaultBDir, Password))
        {
            SourceConfigStore.Save(session, new SourceConfiguration([new SourceDirectory(sourcesBDir)]));
            ArchiveManifest manifest = ManifestStore.Load(session)
                ?? ArchiveManifest.Empty("Archive-Two", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));
            ArchiveUpdater.Update(session, manifest, new SourceConfiguration([new SourceDirectory(sourcesBDir)]));
        }

        using VaultSession verifyA = VaultStore.Unlock(vaultADir, Password);
        using VaultSession verifyB = VaultStore.Unlock(vaultBDir, Password);
        ReplicaComparison comparison = ReplicaComparer.Compare(
            ReplicaInspector.Inspect(verifyA, vaultADir),
            ReplicaInspector.Inspect(verifyB, vaultBDir));

        Assert.Equal(ReplicaSyncState.DifferentArchives, comparison.State);
    }

    [Fact]
    public void DriveMarker_IsRefreshed_OnUpdate()
    {
        // Marker at the drive root above the vault (as new-usb.ps1 writes it).
        string markerPath = Path.Combine(tempRoot, DriveMarker.MarkerFileName);
        File.WriteAllText(markerPath, "{\"schema\":\"emergency-archive-drive\",\"layoutVer\":1,\"archiveId\":null,\"archiveVer\":null}");

        File.WriteAllText(Path.Combine(sourcesADir, "a.txt"), "marker test");
        UpdateVault(vaultADir, sourcesADir);

        using VaultSession session = VaultStore.Unlock(vaultADir, Password);
        ReplicaInfo info = ReplicaInspector.Inspect(session, vaultADir);
        Assert.True(DriveMarker.TryRefresh(vaultADir, info));

        string json = File.ReadAllText(markerPath);
        Assert.Contains(info.ArchiveId, json);
        Assert.Contains("lastSyncUtc", json);
    }
}
