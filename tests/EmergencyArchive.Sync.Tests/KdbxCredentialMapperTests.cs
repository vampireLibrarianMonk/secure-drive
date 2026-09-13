using EmergencyArchive.Crypto.Kdbx;
using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>
/// Verifies the credential &lt;-&gt; KDBX mapping and a full export/import round
/// trip through the KeePass codec. Uses the real (default-cost) exporter so the
/// end-to-end path is exercised; kept to a couple of cases to stay fast enough.
/// </summary>
public class KdbxCredentialMapperTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public void ExportThenImport_RecoversAllFields()
    {
        var bank = new Credential(
            Guid.NewGuid().ToString("N"),
            "bank.example.com",
            "alice",
            "s3cr3t",
            [
                new CredentialField(KdbxCredentialMapper.UrlKey, "https://bank.example.com"),
                new CredentialField(KdbxCredentialMapper.NotesKey, "primary"),
                new CredentialField("Recovery code", "RC-9999"),
            ]);
        var db = CredentialDatabase.Empty.With(bank);

        byte[] file = KdbxCredentialMapper.Export(db, Password);
        IReadOnlyList<Credential> imported = KdbxCredentialMapper.Import(file, Password);

        Credential got = Assert.Single(imported);
        Assert.Equal("bank.example.com", got.Site);
        Assert.Equal("alice", got.Username);
        Assert.Equal("s3cr3t", got.Password);
        Assert.Contains(got.Extras, e => e.Key == KdbxCredentialMapper.UrlKey && e.Value == "https://bank.example.com");
        Assert.Contains(got.Extras, e => e.Key == KdbxCredentialMapper.NotesKey && e.Value == "primary");
        Assert.Contains(got.Extras, e => e.Key == "Recovery code" && e.Value == "RC-9999");
    }

    [Fact]
    public void ExportThenImport_PreservesUnicode_AndAssignsFreshIds()
    {
        var e1 = Credential.Create("почта.example", "борис", "P@ss—🔐");
        var e2 = Credential.Create("site2.example", "bob", "pw2");
        var db = CredentialDatabase.Empty.With(e1).With(e2);

        byte[] file = KdbxCredentialMapper.Export(db, Password);
        IReadOnlyList<Credential> imported = KdbxCredentialMapper.Import(file, Password);

        Assert.Equal(2, imported.Count);
        Credential unicode = imported.Single(c => c.Site == "почта.example");
        Assert.Equal("борис", unicode.Username);
        Assert.Equal("P@ss—🔐", unicode.Password);

        // Import always mints fresh ids (they are not carried from KDBX).
        Assert.All(imported, c => Assert.False(string.IsNullOrEmpty(c.Id)));
        Assert.NotEqual(imported[0].Id, imported[1].Id);
    }

    [Fact]
    public void Import_WrongPassword_Throws()
    {
        byte[] file = KdbxCredentialMapper.Export(
            CredentialDatabase.Empty.With(Credential.Create("s", "u", "p")), Password);

        Assert.Throws<KdbxAuthenticationException>(() => KdbxCredentialMapper.Import(file, "nope"));
    }
}
