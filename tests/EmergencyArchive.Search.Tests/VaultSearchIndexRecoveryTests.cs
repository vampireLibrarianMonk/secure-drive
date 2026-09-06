using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Search;
using Xunit;

namespace EmergencyArchive.Search.Tests;

/// <summary>Spec section 24: the search index is disposable and self-healing.</summary>
public class VaultSearchIndexRecoveryTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;

    public VaultSearchIndexRecoveryTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);

        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        session.WriteFile("Birth Certificate.pdf", "Registry office birth certificate"u8.ToArray()); // scanned doc: name-only indexing
        session.CreateDirectory("Insurance");
        session.WriteFile("Insurance/Home policy.txt", System.Text.Encoding.UTF8.GetBytes("The home insurance policy covers fire and water damage."));
    }

    public void Dispose() => Directory.Delete(vaultDir, recursive: true);

    [Fact]
    public void CorruptedIndexFile_IsRebuilt_SpecSection24()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            using VaultSearchIndex index = VaultSearchIndex.Build(session);
        }

        // Damage the persisted index file (through the vault, where it lives).
        using (VaultSession corruptSession = VaultStore.Unlock(vaultDir, Password))
        {
            corruptSession.WriteFile(VaultSearchIndex.IndexPath, "definitely not a valid index"u8.ToArray());
        }

        using VaultSession recoverySession = VaultStore.Unlock(vaultDir, Password);
        using VaultSearchIndex recovered = VaultSearchIndex.LoadOrBuild(recoverySession);

        Assert.Equal(SearchIndexStatus.RebuiltAfterCorruption, recovered.Status);
        Assert.Equal(2, recovered.DocumentCount);
        Assert.Contains(recovered.Search("fire damage"), r => r.RelativePath == "Insurance/Home policy.txt");
    }

    [Fact]
    public void MissingIndexFile_IsBuilt()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);

        Assert.Equal(SearchIndexStatus.Built, index.Status);
        Assert.Equal(2, index.DocumentCount);
    }

    [Fact]
    public void EmptyVault_ProducesEmptyIndex()
    {
        string emptyVaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyVaultDir);
        try
        {
            VaultStore.Create(emptyVaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
            using VaultSession session = VaultStore.Unlock(emptyVaultDir, Password);
            using VaultSearchIndex index = VaultSearchIndex.Build(session);

            Assert.Equal(0, index.DocumentCount);
            Assert.True(session.FileExists(VaultSearchIndex.IndexPath));
        }
        finally
        {
            Directory.Delete(emptyVaultDir, recursive: true);
        }
    }
}
