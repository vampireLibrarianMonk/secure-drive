namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// Constants for the KeePass KDBX binary format (KDBX 3.1 and 4.x). Values are
/// taken from the documented, open KeePass format; nothing here derives from
/// GPL source. All multi-byte integers in KDBX are little-endian.
/// </summary>
internal static class KdbxFormat
{
    /// <summary>First signature: identifies a KeePass file.</summary>
    public const uint Signature1 = 0x9AA2D903;

    /// <summary>Second signature: identifies the KDBX (post-1.x) family.</summary>
    public const uint Signature2 = 0xB54BFB67;

    public const ushort FileVersionCriticalMask = 0xFFFF;
    public const uint FileVersion3Major = 3;
    public const uint FileVersion4Major = 4;

    /// <summary>Outer-header field ids (the unencrypted header TLVs).</summary>
    internal enum HeaderFieldId : byte
    {
        EndOfHeader = 0,
        Comment = 1,
        CipherId = 2,
        CompressionFlags = 3,
        MasterSeed = 4,
        TransformSeed = 5,     // KDBX 3.1 only
        TransformRounds = 6,   // KDBX 3.1 only
        EncryptionIv = 7,
        ProtectedStreamKey = 8, // KDBX 3.1 only (inner random stream key)
        StreamStartBytes = 9,   // KDBX 3.1 only
        InnerRandomStreamId = 10, // KDBX 3.1 only
        KdfParameters = 11,     // KDBX 4 only (VariantDictionary)
        PublicCustomData = 12,  // KDBX 4 only
    }

    /// <summary>Inner-header field ids (KDBX 4 only; sit at the start of the decrypted payload).</summary>
    internal enum InnerHeaderFieldId : byte
    {
        EndOfHeader = 0,
        InnerRandomStreamId = 1,
        InnerRandomStreamKey = 2,
        Binary = 3,
    }

    /// <summary>Compression flag values (CompressionFlags header field).</summary>
    internal enum CompressionAlgorithm : uint
    {
        None = 0,
        GZip = 1,
    }

    /// <summary>Inner random stream cipher (protects in-XML secret values).</summary>
    internal enum InnerStreamAlgorithm : uint
    {
        Null = 0,
        ArcFourVariant = 1, // legacy, unsupported
        Salsa20 = 2,        // KDBX 3.1
        ChaCha20 = 3,       // KDBX 4
    }

    // Cipher UUIDs (16 bytes each), as written in the CipherId header field.
    public static readonly byte[] CipherAes256Cbc = Convert.FromHexString("31C1F2E6BF714350BE5805216AFC5AFF");
    public static readonly byte[] CipherChaCha20 = Convert.FromHexString("D6038A2B8B6F4CB5A524339A31DBB59A");

    // KDF UUIDs (16 bytes each), used as the "$UUID" entry in the KDBX4 KDF VariantDictionary.
    public static readonly byte[] KdfAes = Convert.FromHexString("C9D9F39A628A4460BF740D08C18A4FEA");
    public static readonly byte[] KdfArgon2d = Convert.FromHexString("EF636DDF8C29444B91F7A9A403E30A0C");
    public static readonly byte[] KdfArgon2id = Convert.FromHexString("9E298B1956DB4773B23DFE5F0F04D97F");

    // VariantDictionary keys (KDBX4).
    public const string KdfUuidKey = "$UUID";
    public const string Argon2SaltKey = "S";
    public const string Argon2ParallelismKey = "P";
    public const string Argon2MemoryKey = "M";     // bytes
    public const string Argon2IterationsKey = "I";
    public const string Argon2VersionKey = "V";
    public const string AesKdfRoundsKey = "R";
    public const string AesKdfSeedKey = "S";

    public static bool UuidEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceEqual(b);
}
