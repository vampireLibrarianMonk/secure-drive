using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// KDBX key computation shared by the readers and writer: the composite key
/// (from the password), the KDF transform (Argon2 for KDBX4, AES-KDF for
/// KDBX3.1), and the derived master / HMAC keys. Verified against the
/// documented KeePass computation (see pykeepass common.py).
/// </summary>
internal static class KdbxKeyDerivation
{
    /// <summary>
    /// Composite key = SHA-256( SHA-256(UTF-8 password) ). Password-only: no
    /// key file is combined (key-file support is a deliberate future step).
    /// </summary>
    public static byte[] CompositeKey(string password)
    {
        byte[] pw = System.Text.Encoding.UTF8.GetBytes(password);
        try
        {
            byte[] passwordComposite = SHA256.HashData(pw);
            // KeePass hashes the concatenation of the (single) credential composite.
            byte[] composite = SHA256.HashData(passwordComposite);
            CryptographicOperations.ZeroMemory(passwordComposite);
            return composite;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pw);
        }
    }

    /// <summary>
    /// Runs the KDBX4 KDF selected by the VariantDictionary's $UUID (Argon2d,
    /// Argon2id, or AES-KDF) and returns the 32-byte transformed key.
    /// </summary>
    public static byte[] TransformKeyKdbx4(byte[] compositeKey, VariantDictionary kdf)
    {
        if (!kdf.TryGetByteArray(KdbxFormat.KdfUuidKey, out byte[] uuid))
        {
            throw new KdbxFormatException("KDBX4 KDF parameters are missing the $UUID.");
        }

        if (KdbxFormat.UuidEquals(uuid, KdbxFormat.KdfArgon2d) ||
            KdbxFormat.UuidEquals(uuid, KdbxFormat.KdfArgon2id))
        {
            KdbxCrypto.Argon2Type type = KdbxFormat.UuidEquals(uuid, KdbxFormat.KdfArgon2id)
                ? KdbxCrypto.Argon2Type.Argon2id
                : KdbxCrypto.Argon2Type.Argon2d;

            if (!kdf.TryGetByteArray(KdbxFormat.Argon2SaltKey, out byte[] salt) ||
                !kdf.TryGetUInt64(KdbxFormat.Argon2IterationsKey, out ulong iterations) ||
                !kdf.TryGetUInt64(KdbxFormat.Argon2MemoryKey, out ulong memoryBytes) ||
                !kdf.TryGetUInt32(KdbxFormat.Argon2ParallelismKey, out uint parallelism))
            {
                throw new KdbxFormatException("KDBX4 Argon2 parameters are incomplete.");
            }

            // KDBX stores memory in bytes; Argon2 takes KiB.
            long memoryKib = (long)(memoryBytes / 1024);
            return KdbxCrypto.Argon2(type, compositeKey, salt, (int)iterations, memoryKib, (int)parallelism, 32);
        }

        if (KdbxFormat.UuidEquals(uuid, KdbxFormat.KdfAes))
        {
            if (!kdf.TryGetByteArray(KdbxFormat.AesKdfSeedKey, out byte[] seed) ||
                !kdf.TryGetUInt64(KdbxFormat.AesKdfRoundsKey, out ulong rounds))
            {
                throw new KdbxFormatException("KDBX4 AES-KDF parameters are incomplete.");
            }

            return KdbxCrypto.AesKdfTransform(compositeKey, seed, rounds);
        }

        throw new KdbxFormatException("Unsupported KDBX4 KDF (only Argon2d, Argon2id, and AES-KDF are supported).");
    }

    /// <summary>KDBX3.1 AES-KDF transform from the outer-header transform seed/rounds.</summary>
    public static byte[] TransformKeyKdbx3(byte[] compositeKey, byte[] transformSeed, ulong rounds)
        => KdbxCrypto.AesKdfTransform(compositeKey, transformSeed, rounds);

    /// <summary>Master (content) key = SHA-256(masterSeed || transformedKey).</summary>
    public static byte[] MasterKey(byte[] masterSeed, byte[] transformedKey)
        => SHA256.HashData([.. masterSeed, .. transformedKey]);

    /// <summary>
    /// The base HMAC key (KDBX4) = SHA-512(masterSeed || transformedKey || 0x01).
    /// Per-block keys derive from this.
    /// </summary>
    public static byte[] HmacBaseKey(byte[] masterSeed, byte[] transformedKey)
        => SHA512.HashData([.. masterSeed, .. transformedKey, 0x01]);

    /// <summary>
    /// Per-block HMAC key = SHA-512( LE64(blockIndex) || hmacBaseKey ). The
    /// header uses the special index 0xFFFFFFFFFFFFFFFF.
    /// </summary>
    public static byte[] BlockHmacKey(ulong blockIndex, byte[] hmacBaseKey)
    {
        byte[] indexLe = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(indexLe, blockIndex);
        return SHA512.HashData([.. indexLe, .. hmacBaseKey]);
    }
}
