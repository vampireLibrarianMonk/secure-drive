using System.Text;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Search;
using Xunit;

namespace EmergencyArchive.Search.Tests;

/// <summary>
/// End-to-end index tests against a real encrypted vault: build, search,
/// and persistence inside the vault (spec section 9).
/// </summary>
public class VaultSearchIndexTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;

    public VaultSearchIndexTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);

        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        session.WriteFile("Birth Certificate.pdf", "Registry office birth certificate"u8.ToArray()); // scanned doc: no extractable text
        session.CreateDirectory("Insurance");
        session.WriteFile("Insurance/Home policy.txt", Encoding.UTF8.GetBytes("The home insurance policy covers fire and water damage."));
        session.WriteFile("证件/出生证明.txt", Encoding.UTF8.GetBytes("出生医学证明内容"));
    }

    public void Dispose() => Directory.Delete(vaultDir, recursive: true);

    [Fact]
    public void Build_IndexesAllDocuments_AndPersistsIntoVault()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        using VaultSearchIndex index = VaultSearchIndex.Build(session);

        Assert.Equal(SearchIndexStatus.Built, index.Status);
        Assert.Equal(3, index.DocumentCount);
        Assert.True(session.FileExists(VaultSearchIndex.IndexPath));
    }

    [Fact]
    public void Search_FindsContentAndNames()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        using VaultSearchIndex index = VaultSearchIndex.Build(session);

        IReadOnlyList<SearchResultItem> byContent = index.Search("fire damage");
        IReadOnlyList<SearchResultItem> byName = index.Search("certificate");
        IReadOnlyList<SearchResultItem> unicode = index.Search("出生证明");

        Assert.Contains(byContent, r => r.RelativePath == "Insurance/Home policy.txt");
        Assert.Contains(byName, r => r.RelativePath == "Birth Certificate.pdf");
        Assert.Contains(unicode, r => r.RelativePath == "证件/出生证明.txt");
    }

    [Fact]
    public void Search_WithPartialTerm_MatchesByPrefix()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        using VaultSearchIndex index = VaultSearchIndex.Build(session);

        Assert.Contains(index.Search("insur"), r => r.RelativePath == "Insurance/Home policy.txt");
    }

    [Fact]
    public void Search_NoResults_YieldsEmpty()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        using VaultSearchIndex index = VaultSearchIndex.Build(session);

        Assert.Empty(index.Search("zqjxkv"));
        Assert.Empty(index.Search("   "));
    }

    [Fact]
    public void Search_Results_CarryCategoriesFromFolders()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            // A root-level document: no folder -> category "Other" (spec §10).
            session.WriteFile("Insurance summary.txt", Encoding.UTF8.GetBytes("Summary of all insurance policies."));
        }

        using VaultSession session2 = VaultStore.Unlock(vaultDir, Password);
        using VaultSearchIndex index = VaultSearchIndex.Build(session2);

        IReadOnlyList<SearchResultItem> results = index.Search("insurance");

        Assert.Contains(results, r => r.Category == "Insurance");
        Assert.Contains(results, r => r.Category == "Other");
    }

    [Fact]
    public void IndexStoredInVault_IsEncrypted_SpecSection9()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            using VaultSearchIndex index = VaultSearchIndex.Build(session);
        }

        // Spec section 9: NO physical file on the drive may contain index or
        // document plaintext — everything lives inside the encrypted vault.
        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(vaultDir, "*", SearchOption.AllDirectories))
        {
            string rawText = Encoding.Latin1.GetString(File.ReadAllBytes(file));
            if (rawText.Contains("insurance", StringComparison.OrdinalIgnoreCase)
                || rawText.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                || rawText.Contains("出生", StringComparison.InvariantCulture))
            {
                offenders.Add(file);
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void LoadedIndex_SupportsSearch_WithoutRebuilding()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            using VaultSearchIndex index = VaultSearchIndex.Build(session);
        }

        using VaultSession secondSession = VaultStore.Unlock(vaultDir, Password);
        using VaultSearchIndex loaded = VaultSearchIndex.LoadOrBuild(secondSession);

        Assert.Equal(SearchIndexStatus.Loaded, loaded.Status);
        Assert.Equal(3, loaded.DocumentCount);
        Assert.Contains(loaded.Search("fire damage"), r => r.RelativePath == "Insurance/Home policy.txt");
    }
}
