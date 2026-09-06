using EmergencyArchive.Crypto.Vault;
using System.Text;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

/// <summary>
/// End-to-end vault scenarios mirroring the specification's test requirements
/// (correct password, incorrect password, corrupted vault, unicode names,
/// long names, large files).
/// </summary>
public class VaultStoreTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;

    public VaultStoreTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose()
    {
        Directory.Delete(vaultDir, recursive: true);
    }

    [Fact]
    public void Create_ProducesCryptomatorFiles()
    {
        Assert.True(File.Exists(Path.Combine(vaultDir, "masterkey.cryptomator")));
        Assert.True(File.Exists(Path.Combine(vaultDir, "vault.cryptomator")));
        Assert.True(Directory.Exists(Path.Combine(vaultDir, "d")));
    }

    [Fact]
    public void Unlock_WithCorrectPassword_ListsEmptyRoot()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);

        Assert.Empty(session.List());
    }

    [Fact]
    public void Unlock_WithWrongPassword_ThrowsVaultUnlockException()
    {
        Assert.Throws<VaultUnlockException>(() => VaultStore.Unlock(vaultDir, "not the password"));
    }

    [Fact]
    public void WriteAndReadFile_RoundTrips()
    {
        byte[] content = "home insurance policy contents"u8.ToArray();
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.WriteFile("Homeowners Insurance.pdf", content);
        }

        // Re-unlock to prove persistence beyond the session.
        using VaultSession secondSession = VaultStore.Unlock(vaultDir, Password);
        Assert.Equal(content, secondSession.ReadFile("Homeowners Insurance.pdf"));
        Assert.True(secondSession.FileExists("Homeowners Insurance.pdf"));
    }

    [Fact]
    public void List_ShowsDecryptedNames()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.WriteFile("Birth Certificate.pdf", [1, 2, 3]);
            session.WriteFile("Passport.pdf", [4, 5, 6]);
            session.CreateDirectory("Insurance");
        }

        using VaultSession verify = VaultStore.Unlock(vaultDir, Password);
        var entries = verify.List();

        Assert.Equal(
            ["Birth Certificate.pdf", "Insurance", "Passport.pdf"],
            [.. entries.Select(e => e.Name)]);
        Assert.Equal(VaultEntryKind.File, entries.Single(e => e.Name == "Passport.pdf").Kind);
        Assert.Equal(VaultEntryKind.Directory, entries.Single(e => e.Name == "Insurance").Kind);
    }

    [Fact]
    public void UnicodeAndNestedPaths_RoundTrip()
    {
        byte[] content = "证件内容"u8.ToArray();
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.CreateDirectory("Identity/证件");
            session.WriteFile("Identity/证件/出生证明.pdf", content);
        }

        using VaultSession verify = VaultStore.Unlock(vaultDir, Password);
        Assert.Equal(content, verify.ReadFile("Identity/证件/出生证明.pdf"));
        Assert.Contains("证件", verify.List("Identity").Select(e => e.Name));
    }

    [Fact]
    public void VeryLongFileName_UsesShortenedLayout()
    {
        string longName = string.Concat(new string('x', 300), ".pdf");
        byte[] content = [9, 9, 9];
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.WriteFile(longName, content);
        }

        using VaultSession verify = VaultStore.Unlock(vaultDir, Password);
        Assert.Equal(content, verify.ReadFile(longName));
        Assert.Contains(longName, verify.List().Select(e => e.Name));
    }

    [Fact]
    public void LargeFile_RoundTrips()
    {
        byte[] content = new byte[1024 * 1024]; // 1 MB, several hundred chunks
        Random.Shared.NextBytes(content);
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.WriteFile("large-scan.pdf", content);
        }

        using VaultSession verify = VaultStore.Unlock(vaultDir, Password);
        Assert.Equal(content, verify.ReadFile("large-scan.pdf"));
    }

    [Fact]
    public void CorruptedVaultFile_IsDetected()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.WriteFile("report.pdf", new byte[4096]);
        }

        // Flip one bit inside the encrypted content of the only stored file.
        string contentDir = Path.Combine(vaultDir, "d");
        string file = Directory.EnumerateFiles(contentDir, "*.c9r", SearchOption.AllDirectories).First();
        byte[] bytes = File.ReadAllBytes(file);
        bytes[^3] ^= 0x80;
        File.WriteAllBytes(file, bytes);

        using VaultSession unlocked = VaultStore.Unlock(vaultDir, Password);
        Assert.Throws<VaultIntegrityException>(() => unlocked.ReadFile("report.pdf"));
    }

    [Fact]
    public void TamperedMasterkeyFile_PreventsUnlock()
    {
        string masterkeyPath = Path.Combine(vaultDir, "masterkey.cryptomator");
        File.WriteAllText(masterkeyPath, File.ReadAllText(masterkeyPath).Replace("primaryMasterKey", "PrimaryMasterKey"));

        Assert.ThrowsAny<Exception>(() => VaultStore.Unlock(vaultDir, Password));
    }
}
