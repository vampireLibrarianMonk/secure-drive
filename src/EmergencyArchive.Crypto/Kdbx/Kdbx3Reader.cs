using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// Reads a KDBX 3.1 database (password only). Flow, per the documented KeePass
/// format: composite key -> AES-KDF transform (outer-header seed/rounds) ->
/// master key; decrypt the whole payload with AES-256-CBC; the first 32 bytes
/// must equal the header's StreamStartBytes (the wrong-password check); the
/// remainder is a SHA-256 hashed block stream; GZip-inflate if flagged; then
/// the Salsa20 inner protected-value stream and XML parse.
/// </summary>
internal static class Kdbx3Reader
{
    public static KdbxDatabase Read(KdbxReader reader, KdbxHeader header, string password)
    {
        byte[] compositeKey = KdbxKeyDerivation.CompositeKey(password);
        byte[] transformedKey = KdbxKeyDerivation.TransformKeyKdbx3(compositeKey, header.TransformSeed, header.TransformRounds);
        byte[] masterKey = KdbxKeyDerivation.MasterKey(header.MasterSeed, transformedKey);

        try
        {
            if (!KdbxFormat.UuidEquals(header.CipherId, KdbxFormat.CipherAes256Cbc))
            {
                throw new KdbxFormatException("Unsupported KDBX 3.1 cipher (only AES-256-CBC is supported).");
            }

            byte[] encrypted = reader.ReadRemaining();

            byte[] decrypted;
            try
            {
                decrypted = KdbxCrypto.AesCbcDecrypt(masterKey, header.EncryptionIv, encrypted);
            }
            catch (CryptographicException)
            {
                // PKCS7 unpad failure almost always means a wrong password.
                throw new KdbxAuthenticationException();
            }

            // Credential check: the leading bytes must match StreamStartBytes.
            byte[] streamStart = header.StreamStartBytes;
            if (decrypted.Length < streamStart.Length ||
                !CryptographicOperations.FixedTimeEquals(decrypted.AsSpan(0, streamStart.Length).ToArray(), streamStart))
            {
                throw new KdbxAuthenticationException();
            }

            var blockReader = new KdbxReader(decrypted, streamStart.Length);
            byte[] payload = KdbxPayload.ReadHashedBlockStream(blockReader);

            if (header.Compression == KdbxFormat.CompressionAlgorithm.GZip)
            {
                payload = KdbxPayload.GzipDecompress(payload);
            }

            // KDBX 3.1: the inner stream key and id come from the OUTER header.
            Func<byte[], byte[]> unprotect = KdbxPayload.CreateInnerStream(header.InnerStreamId, header.ProtectedStreamKey);
            return KdbxXml.Parse(payload, unprotect);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(compositeKey);
            CryptographicOperations.ZeroMemory(transformedKey);
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
