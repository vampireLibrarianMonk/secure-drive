using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using Xunit;

namespace EmergencyArchive.Search.Tests;

/// <summary>Phase 3: applying update plans to the index (incremental index updates) and CJK searchability.</summary>
public class SearchIndexChangeTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;

    public SearchIndexChangeTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose() => Directory.Delete(vaultDir, recursive: true);

    [Fact]
    public void CJK_Substring_IsSearchable_AfterSegmentation()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        // Content contains a LONGER run: 出生医学证明内容 — searching for the
        // substring 证明 must match the segmented per-character tokens.
        session.WriteFile("证件/出生证明.txt", "出生医学证明内容"u8.ToArray());
        using VaultSearchIndex index = VaultSearchIndex.Build(session);

        Assert.Contains(index.Search("证明"), r => r.RelativePath == "证件/出生证明.txt");
        Assert.Contains(index.Search("证件"), r => r.RelativePath == "证件/出生证明.txt");
        Assert.Contains(index.Search("内容"), r => r.RelativePath == "证件/出生证明.txt");
    }

    [Fact]
    public void DeletePlan_RemovesDocumentFromIndex()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        session.WriteFile("temporary.txt", "searchable words"u8.ToArray());
        using VaultSearchIndex index = VaultSearchIndex.Build(session);
        Assert.Single(index.Search("searchable"));

        var deletePlan = new SyncPlan([], [], ["temporary.txt"]);
        index.ApplyChanges(session, deletePlan, ArchiveManifest.Empty("id", "2026.09.06.001"));

        Assert.Empty(index.Search("searchable"));
        Assert.Equal(0, index.DocumentCount);
    }

    [Fact]
    public void CountStaleEntries_DetectsManifestMismatch()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        session.WriteFile("a.txt", "version one"u8.ToArray());
        using VaultSearchIndex index = VaultSearchIndex.Build(session);

        string actualSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("version one"u8.ToArray()));

        // A manifest that agrees with the index: no stale entries.
        var matchingManifest = new ArchiveManifest(
            "id", "2026.09.06.001", DateTimeOffset.UtcNow, "0.1.0",
            [new ManifestEntry("a.txt", 11, DateTimeOffset.UtcNow, actualSha)]);
        Assert.Equal(0, index.CountStaleEntries(matchingManifest));

        // A manifest claiming different content: one stale entry.
        var mismatchingManifest = new ArchiveManifest(
            "id", "2026.09.06.002", DateTimeOffset.UtcNow, "0.1.0",
            [new ManifestEntry("a.txt", 11, DateTimeOffset.UtcNow, new string('f', 64))]);
        Assert.Equal(1, index.CountStaleEntries(mismatchingManifest));
    }
}
