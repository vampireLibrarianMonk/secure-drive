using EmergencyArchive.Crypto.Kdbx;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

/// <summary>
/// End-to-end KDBX 4 round-trip: our writer produces a file, our reader
/// recovers it. This validates the whole KDBX4 pipeline (Argon2id KDF,
/// AES-256-CBC, GZip, HMAC block stream, header HMAC, ChaCha20 inner stream,
/// XML) against itself. Cross-tool interop with real KeePass/KeePassXC is a
/// separate, manual check the user performs. A low Argon2 cost keeps the tests
/// fast while exercising the identical code paths.
/// </summary>
public class KdbxRoundTripTests
{
    private const string Password = "correct horse battery staple";

    // Low cost so the KDF is fast in tests (still real Argon2id).
    private static readonly Kdbx4Writer.Argon2Cost FastCost =
        new(MemoryBytes: 1024 * 1024, Iterations: 1, Parallelism: 1);

    private static KdbxDatabase Sample()
    {
        var db = new KdbxDatabase();
        var bank = new KdbxEntry
        {
            Title = "bank.example.com",
            UserName = "alice",
            Password = "s3cr3t-p@ss",
            Url = "https://bank.example.com/login",
            Notes = "primary account",
        };
        bank.CustomFields.Add(new("Recovery code", "RC-1234-5678"));
        db.Entries.Add(bank);

        db.Entries.Add(new KdbxEntry
        {
            Title = "почта.example",           // unicode title
            UserName = "борис",                 // unicode username
            Password = "P@sswörd—🔐",           // unicode + emoji password
        });

        return db;
    }

    [Fact]
    public void WriteThenRead_RecoversAllStandardFields()
    {
        byte[] file = Kdbx4Writer.Write(Sample(), Password, FastCost);
        KdbxDatabase read = KdbxCodec.Read(file, Password);

        Assert.Equal(2, read.Entries.Count);

        KdbxEntry bank = read.Entries.Single(e => e.Title == "bank.example.com");
        Assert.Equal("alice", bank.UserName);
        Assert.Equal("s3cr3t-p@ss", bank.Password);
        Assert.Equal("https://bank.example.com/login", bank.Url);
        Assert.Equal("primary account", bank.Notes);
        Assert.Contains(bank.CustomFields, f => f.Key == "Recovery code" && f.Value == "RC-1234-5678");
    }

    [Fact]
    public void WriteThenRead_PreservesUnicodeAndEmoji()
    {
        byte[] file = Kdbx4Writer.Write(Sample(), Password, FastCost);
        KdbxDatabase read = KdbxCodec.Read(file, Password);

        KdbxEntry unicode = read.Entries.Single(e => e.Title == "почта.example");
        Assert.Equal("борис", unicode.UserName);
        Assert.Equal("P@sswörd—🔐", unicode.Password);
    }

    [Fact]
    public void WrongPassword_ThrowsAuthenticationException()
    {
        byte[] file = Kdbx4Writer.Write(Sample(), Password, FastCost);
        Assert.Throws<KdbxAuthenticationException>(() => KdbxCodec.Read(file, "wrong password"));
    }

    [Fact]
    public void TamperedCiphertext_IsRejected()
    {
        byte[] file = Kdbx4Writer.Write(Sample(), Password, FastCost);

        // Flip a byte near the end (inside the HMAC block stream / ciphertext).
        file[^1] ^= 0xFF;

        Assert.ThrowsAny<KdbxException>(() => KdbxCodec.Read(file, Password));
    }

    [Fact]
    public void NotAKdbxFile_ThrowsFormatException()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];
        Assert.Throws<KdbxFormatException>(() => KdbxCodec.Read(garbage, Password));
    }

    [Fact]
    public void EmptyDatabase_RoundTrips()
    {
        byte[] file = Kdbx4Writer.Write(new KdbxDatabase(), Password, FastCost);
        KdbxDatabase read = KdbxCodec.Read(file, Password);
        Assert.Empty(read.Entries);
    }
}
