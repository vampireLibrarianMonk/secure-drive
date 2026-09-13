using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// Writes a KDBX 4 database (password only) that KeePass / KeePassXC can open:
/// Argon2id KDF, AES-256-CBC content cipher, GZip compression, and a ChaCha20
/// inner protected-value stream. Mirrors the reader exactly so a written file
/// round-trips, and follows the documented KeePass layout so it interoperates.
/// </summary>
internal static class Kdbx4Writer
{
    /// <summary>Argon2 cost parameters (defaults match a modern KeePassXC database).</summary>
    public sealed record Argon2Cost(ulong MemoryBytes = 64UL * 1024 * 1024, ulong Iterations = 2, uint Parallelism = 1);

    public static byte[] Write(KdbxDatabase database, string password, Argon2Cost? cost = null, DateTime? nowUtc = null)
    {
        cost ??= new Argon2Cost();
        DateTime now = nowUtc ?? DateTime.UtcNow;

        byte[] masterSeed = RandomNumberGenerator.GetBytes(32);
        byte[] encryptionIv = RandomNumberGenerator.GetBytes(16); // AES-CBC block size
        byte[] argonSalt = RandomNumberGenerator.GetBytes(32);
        byte[] innerStreamKey = RandomNumberGenerator.GetBytes(64); // ChaCha20 inner stream key

        // KDF parameters (Argon2id VariantDictionary).
        var kdf = new VariantDictionary();
        kdf.SetByteArray(KdbxFormat.KdfUuidKey, KdbxFormat.KdfArgon2id);
        kdf.SetByteArray(KdbxFormat.Argon2SaltKey, argonSalt);
        kdf.SetUInt32(KdbxFormat.Argon2ParallelismKey, cost.Parallelism);
        kdf.SetUInt64(KdbxFormat.Argon2MemoryKey, cost.MemoryBytes);
        kdf.SetUInt64(KdbxFormat.Argon2IterationsKey, cost.Iterations);
        kdf.SetUInt32(KdbxFormat.Argon2VersionKey, 0x13);

        byte[] rawHeader = BuildOuterHeader(kdf, masterSeed, encryptionIv);

        // Derive keys (same computation as the reader).
        byte[] compositeKey = KdbxKeyDerivation.CompositeKey(password);
        byte[] transformedKey = KdbxKeyDerivation.TransformKeyKdbx4(compositeKey, kdf);
        byte[] masterKey = KdbxKeyDerivation.MasterKey(masterSeed, transformedKey);
        byte[] hmacBaseKey = KdbxKeyDerivation.HmacBaseKey(masterSeed, transformedKey);

        try
        {
            // Inner payload: inner header + XML (protected values via ChaCha20).
            Func<byte[], byte[]> protect = KdbxPayload.CreateInnerStream(
                KdbxFormat.InnerStreamAlgorithm.ChaCha20, innerStreamKey);
            byte[] xml = KdbxXml.Build(database, protect, now);
            byte[] payload = BuildInnerPayload(innerStreamKey, xml);

            byte[] compressed = KdbxPayload.GzipCompress(payload);
            byte[] ciphertext = KdbxCrypto.AesCbcEncrypt(masterKey, encryptionIv, compressed);

            // Assemble: header || SHA256(header) || HMAC(header) || HMAC block stream.
            var writer = new KdbxWriter();
            writer.WriteBytes(rawHeader);
            writer.WriteBytes(SHA256.HashData(rawHeader));

            byte[] headerHmacKey = KdbxKeyDerivation.BlockHmacKey(ulong.MaxValue, hmacBaseKey);
            writer.WriteBytes(HMACSHA256.HashData(headerHmacKey, rawHeader));

            writer.WriteBytes(KdbxPayload.WriteHmacBlockStream(ciphertext, hmacBaseKey));
            return writer.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(compositeKey);
            CryptographicOperations.ZeroMemory(transformedKey);
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(hmacBaseKey);
        }
    }

    private static byte[] BuildOuterHeader(VariantDictionary kdf, byte[] masterSeed, byte[] encryptionIv)
    {
        var w = new KdbxWriter();
        w.WriteUInt32(KdbxFormat.Signature1);
        w.WriteUInt32(KdbxFormat.Signature2);
        // File version 4.0 -> minor (uint16) = 0, major (uint16) = 4.
        w.WriteUInt16(0);
        w.WriteUInt16(4);

        WriteHeaderField(w, KdbxFormat.HeaderFieldId.CipherId, KdbxFormat.CipherAes256Cbc);
        WriteHeaderField(w, KdbxFormat.HeaderFieldId.CompressionFlags,
            BitConverter.GetBytes((uint)KdbxFormat.CompressionAlgorithm.GZip));
        WriteHeaderField(w, KdbxFormat.HeaderFieldId.MasterSeed, masterSeed);
        WriteHeaderField(w, KdbxFormat.HeaderFieldId.EncryptionIv, encryptionIv);
        WriteHeaderField(w, KdbxFormat.HeaderFieldId.KdfParameters, kdf.Serialize());
        WriteHeaderField(w, KdbxFormat.HeaderFieldId.EndOfHeader, [0x0D, 0x0A, 0x0D, 0x0A]);

        return w.ToArray();
    }

    private static void WriteHeaderField(KdbxWriter w, KdbxFormat.HeaderFieldId id, byte[] data)
    {
        w.WriteByte((byte)id);
        w.WriteInt32(data.Length); // KDBX4 uses a 32-bit field length
        w.WriteBytes(data);
    }

    private static byte[] BuildInnerPayload(byte[] innerStreamKey, byte[] xml)
    {
        var w = new KdbxWriter();

        // Inner header: random-stream id (ChaCha20), the stream key, then End.
        w.WriteByte((byte)KdbxFormat.InnerHeaderFieldId.InnerRandomStreamId);
        w.WriteInt32(4);
        w.WriteUInt32((uint)KdbxFormat.InnerStreamAlgorithm.ChaCha20);

        w.WriteByte((byte)KdbxFormat.InnerHeaderFieldId.InnerRandomStreamKey);
        w.WriteInt32(innerStreamKey.Length);
        w.WriteBytes(innerStreamKey);

        w.WriteByte((byte)KdbxFormat.InnerHeaderFieldId.EndOfHeader);
        w.WriteInt32(0);

        w.WriteBytes(xml);
        return w.ToArray();
    }
}
