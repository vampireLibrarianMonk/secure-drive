using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>Credential store round-trips against a real encrypted vault (MIT-clean, our own model).</summary>
public class CredentialStoreTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;

    public CredentialStoreTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-cred-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose() => Directory.Delete(vaultDir, recursive: true);

    [Fact]
    public void Load_FromNewVault_ReturnsEmpty()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        CredentialDatabase db = CredentialStore.Load(session);
        Assert.True(db.IsEmpty);
        Assert.Equal(0, db.Count);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEntriesAndExtras()
    {
        var bank = Credential.Create("bank.example.com", "jane.doe", "s3cr3t-p@ss")
            with
        { Extras = [new CredentialField("PIN", "4821"), new CredentialField("recovery", "river-copper-morning")] };
        var email = Credential.Create("mail.example.com", "jane", "another-pass");

        var db = CredentialDatabase.Empty.With(bank).With(email);

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            CredentialStore.Save(session, db);
        }

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            CredentialDatabase loaded = CredentialStore.Load(session);
            Assert.Equal(2, loaded.Count);

            Credential? loadedBank = loaded.Find(bank.Id);
            Assert.NotNull(loadedBank);
            Assert.Equal("bank.example.com", loadedBank!.Site);
            Assert.Equal("jane.doe", loadedBank.Username);
            Assert.Equal("s3cr3t-p@ss", loadedBank.Password);
            Assert.Equal(2, loadedBank.Extras.Count);
            Assert.Contains(loadedBank.Extras, f => f.Key == "PIN" && f.Value == "4821");
            Assert.Contains(loadedBank.Extras, f => f.Key == "recovery" && f.Value == "river-copper-morning");
        }
    }

    [Fact]
    public void With_ReplacesEntryOfSameId_AndWithout_Removes()
    {
        var c = Credential.Create("site", "user", "pw1");
        var db = CredentialDatabase.Empty.With(c);

        // Replace (same id) updates in place, not appends.
        var updated = db.With(c with { Password = "pw2" });
        Assert.Equal(1, updated.Count);
        Assert.Equal("pw2", updated.Find(c.Id)!.Password);

        // Remove.
        Assert.True(updated.Without(c.Id).IsEmpty);
    }

    [Fact]
    public void CredentialsFile_IsEncryptedInVault_AndExcludedFromDocuments()
    {
        var db = CredentialDatabase.Empty.With(Credential.Create("s", "u", "TOPSECRETVALUE"));

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            CredentialStore.Save(session, db);
        }

        // The plaintext secret must not appear anywhere on disk (the vault
        // encrypts the stored file).
        bool leaked = Directory.EnumerateFiles(vaultDir, "*", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains("TOPSECRETVALUE", StringComparison.Ordinal));
        Assert.False(leaked, "credential secret found in cleartext on disk");

        // And it is an infrastructure path, so it never shows up as a document.
        Assert.True(EmergencyArchive.Core.VaultPaths.IsInfrastructurePath(CredentialStore.StorePath));
    }
}
