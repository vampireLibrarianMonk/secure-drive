using System.Text;
using EmergencyArchive.Crypto.Kdbx;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

/// <summary>
/// Red/blue cycle (2026-11): the KDBX importer processes attacker-chosen
/// parameters BEFORE the file is authenticated (the header HMAC is verified
/// only after the KDF), so every pre-authentication value is bounded here:
/// KDF parameters (memory/iterations/rounds/parallelism), header field
/// lengths, VariantDictionary sizes, and the XML entry count. A hostile
/// KeePass file must fail fast with a clear message — never hang or OOM.
/// </summary>
public sealed class KdbxHardeningTests
{
    private const string Password = "correct horse battery staple";

    private static byte[] CompositeKey() => KdbxKeyDerivation.CompositeKey(Password);

    private static VariantDictionary Argon2Kdf(ulong memoryBytes, ulong iterations, uint parallelism = 4)
    {
        var kdf = new VariantDictionary();
        kdf.SetByteArray(KdbxFormat.KdfUuidKey, KdbxFormat.KdfArgon2id);
        kdf.SetByteArray(KdbxFormat.Argon2SaltKey, new byte[32]);
        kdf.SetUInt64(KdbxFormat.Argon2IterationsKey, iterations);
        kdf.SetUInt64(KdbxFormat.Argon2MemoryKey, memoryBytes);
        kdf.SetUInt32(KdbxFormat.Argon2ParallelismKey, parallelism);
        return kdf;
    }

    private static VariantDictionary AesKdf(ulong rounds)
    {
        var kdf = new VariantDictionary();
        kdf.SetByteArray(KdbxFormat.KdfUuidKey, KdbxFormat.KdfAes);
        kdf.SetByteArray(KdbxFormat.AesKdfSeedKey, new byte[32]);
        kdf.SetUInt64(KdbxFormat.AesKdfRoundsKey, rounds);
        return kdf;
    }

    // --- KDF caps ---------------------------------------------------------------

    [Fact]
    public void Argon2_MemoryFarAboveCap_IsRejected_Instantly()
    {
        // 64 GiB of Argon2 memory: without the cap this allocates until OOM.
        Assert.Throws<KdbxFormatException>(
            () => KdbxKeyDerivation.TransformKeyKdbx4(CompositeKey(), Argon2Kdf(64UL * 1024 * 1024 * 1024, 2)));
    }

    [Fact]
    public void Argon2_IterationsAboveCap_IsRejected_Instantly()
    {
        // A million passes would burn CPU for hours on the victim's machine.
        Assert.Throws<KdbxFormatException>(
            () => KdbxKeyDerivation.TransformKeyKdbx4(CompositeKey(), Argon2Kdf(64UL * 1024 * 1024, 1_000_000)));
    }

    [Fact]
    public void Argon2_ZeroParameters_AreRejected()
    {
        Assert.Throws<KdbxFormatException>(
            () => KdbxKeyDerivation.TransformKeyKdbx4(CompositeKey(), Argon2Kdf(64UL * 1024 * 1024, 0, 0)));
    }

    [Fact]
    public void Argon2_SmallLegitParameters_DeriveAKey()
    {
        byte[] key = KdbxKeyDerivation.TransformKeyKdbx4(
            CompositeKey(), Argon2Kdf(8UL * 1024 * 1024, 1, parallelism: 1));
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void AesKdf_RoundsAboveCap_AreRejected_Instantly()
    {
        Assert.Throws<KdbxFormatException>(
            () => KdbxKeyDerivation.TransformKeyKdbx4(CompositeKey(), AesKdf(10_000_001)));
    }

    [Fact]
    public void AesKdf_LegitRounds_DeriveAKey()
    {
        byte[] key = KdbxKeyDerivation.TransformKeyKdbx4(CompositeKey(), AesKdf(1000));
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void Kdbx3_TransformRounds_AboveCap_AreRejected()
    {
        Assert.Throws<KdbxFormatException>(
            () => KdbxKeyDerivation.TransformKeyKdbx3(CompositeKey(), new byte[32], 10_000_001));
    }

    // --- Header field bounds ------------------------------------------------------

    [Fact]
    public void Header_FieldLength_AboveCap_IsRejected()
    {
        byte[] data = CraftHeader(fieldLength: 17 * 1024 * 1024);
        var ex = Assert.Throws<KdbxFormatException>(() => KdbxHeader.Parse(new KdbxReader(data)));
        Assert.Contains("unreasonably large", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_FieldLength_Negative_IsRejected()
    {
        byte[] data = CraftHeader(fieldLength: -1);
        Assert.Throws<KdbxFormatException>(() => KdbxHeader.Parse(new KdbxReader(data)));
    }

    private static byte[] CraftHeader(int fieldLength)
    {
        var writer = new KdbxWriter();
        writer.WriteUInt32(KdbxFormat.Signature1);
        writer.WriteUInt32(KdbxFormat.Signature2);
        writer.WriteUInt16(0);
        writer.WriteUInt16((ushort)KdbxFormat.FileVersion4Major);
        writer.WriteByte((byte)KdbxFormat.HeaderFieldId.Comment);
        writer.WriteInt32(fieldLength);
        return writer.ToArray(); // no field data: the cap must fire before the read
    }

    // --- VariantDictionary bounds -------------------------------------------------

    [Fact]
    public void VariantDictionary_ValueLength_AboveCap_IsRejected()
    {
        var writer = new KdbxWriter();
        writer.WriteUInt16(0x0100);
        writer.WriteByte(0x42); // ByteArray
        writer.WriteInt32(1);
        writer.WriteBytes("K"u8);
        writer.WriteInt32(2 * 1024 * 1024); // no data: the cap must fire first
        Assert.Throws<KdbxFormatException>(() => VariantDictionary.Parse(writer.ToArray()));
    }

    [Fact]
    public void VariantDictionary_NegativeLengths_AreRejected()
    {
        var writer = new KdbxWriter();
        writer.WriteUInt16(0x0100);
        writer.WriteByte(0x42);
        writer.WriteInt32(1);
        writer.WriteBytes("K"u8);
        writer.WriteInt32(-5);
        Assert.Throws<KdbxFormatException>(() => VariantDictionary.Parse(writer.ToArray()));
    }

    [Fact]
    public void VariantDictionary_TooManyEntries_AreRejected()
    {
        var writer = new KdbxWriter();
        writer.WriteUInt16(0x0100);
        for (int i = 0; i <= 1024; i++)
        {
            writer.WriteByte(0x08); // Bool
            writer.WriteInt32(1);
            writer.WriteBytes("K"u8);
            writer.WriteInt32(1);
            writer.WriteBytes([0x00]);
        }

        Assert.Throws<KdbxFormatException>(() => VariantDictionary.Parse(writer.ToArray()));
    }

    [Fact]
    public void VariantDictionary_LegitParameters_RoundTrip()
    {
        byte[] bytes = Argon2Kdf(64UL * 1024 * 1024, 2).Serialize();
        VariantDictionary parsed = VariantDictionary.Parse(bytes);
        Assert.True(parsed.TryGetUInt64(KdbxFormat.Argon2MemoryKey, out ulong memory));
        Assert.Equal(64UL * 1024 * 1024, memory);
    }

    // --- XML entry cap --------------------------------------------------------------

    [Fact]
    public void KdbxXml_MoreThanEntryCap_IsRejected()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><KeePassFile><Meta/><Root><Group>");
        for (int i = 0; i <= 100_000; i++)
        {
            sb.Append("<Entry><String><Key>Title</Key><Value>t</Value></String></Entry>");
        }

        sb.Append("</Group></Root></KeePassFile>");
        Assert.Throws<KdbxFormatException>(() => KdbxXml.Parse(Encoding.UTF8.GetBytes(sb.ToString()), b => b));
    }

    [Fact]
    public void KdbxXml_SmallDatabase_Parses()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<KeePassFile><Meta/><Root><Group><Entry>" +
            "<String><Key>Title</Key><Value>Bank</Value></String>" +
            "<String><Key>Password</Key><Value>c2VjcmV0</Value></String>" +
            "</Entry></Group></Root></KeePassFile>";
        KdbxDatabase db = KdbxXml.Parse(Encoding.UTF8.GetBytes(xml), b => b);
        Assert.Single(db.Entries);
    }
}

