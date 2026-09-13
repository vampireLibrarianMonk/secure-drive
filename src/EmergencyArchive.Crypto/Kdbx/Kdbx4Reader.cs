using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// Reads a KDBX 4 database (password only). Flow, per the documented KeePass
/// format: derive the composite key, run the KDF (Argon2 / AES-KDF), verify the
/// header SHA-256 and header HMAC, read and authenticate the HMAC block stream,
/// decrypt (AES-256-CBC or ChaCha20), GZip-inflate if needed, parse the inner
/// header, set up the inner protected-value stream, then parse the XML.
/// </summary>
internal static class Kdbx4Reader
{
    public static KdbxDatabase Read(KdbxReader reader, KdbxHeader header, string password)
    {
        byte[] compositeKey = KdbxKeyDerivation.CompositeKey(password);
        byte[] transformedKey = KdbxKeyDerivation.TransformKeyKdbx4(compositeKey, header.KdfParameters
            ?? throw new KdbxFormatException("KDBX4 file has no KDF parameters."));
        byte[] masterKey = KdbxKeyDerivation.MasterKey(header.MasterSeed, transformedKey);
        byte[] hmacBaseKey = KdbxKeyDerivation.HmacBaseKey(header.MasterSeed, transformedKey);

        try
        {
            // Header integrity: a plain SHA-256 of the header, then the header HMAC.
            byte[] expectedHeaderSha = reader.ReadBytes(32);
            byte[] actualHeaderSha = SHA256.HashData(header.RawHeaderBytes);
            if (!CryptographicOperations.FixedTimeEquals(expectedHeaderSha, actualHeaderSha))
            {
                throw new KdbxFormatException("KDBX4 header checksum mismatch (file is corrupt).");
            }

            byte[] expectedHeaderHmac = reader.ReadBytes(32);
            byte[] headerHmacKey = KdbxKeyDerivation.BlockHmacKey(ulong.MaxValue, hmacBaseKey);
            byte[] actualHeaderHmac = HMACSHA256.HashData(headerHmacKey, header.RawHeaderBytes);
            if (!CryptographicOperations.FixedTimeEquals(expectedHeaderHmac, actualHeaderHmac))
            {
                // Wrong password (or tampered header) is the overwhelmingly common cause.
                throw new KdbxAuthenticationException();
            }

            // Authenticated ciphertext -> decrypt -> optional GZip -> payload.
            byte[] ciphertext = KdbxPayload.ReadHmacBlockStream(reader, hmacBaseKey);
            byte[] payload = DecryptContent(header, masterKey, ciphertext);
            if (header.Compression == KdbxFormat.CompressionAlgorithm.GZip)
            {
                payload = KdbxPayload.GzipDecompress(payload);
            }

            // KDBX4 inner header sits at the front of the payload.
            var payloadReader = new KdbxReader(payload);
            (KdbxFormat.InnerStreamAlgorithm innerId, byte[] innerKey) = ReadInnerHeader(payloadReader);
            byte[] xml = payloadReader.ReadRemaining();

            Func<byte[], byte[]> unprotect = KdbxPayload.CreateInnerStream(innerId, innerKey);
            return KdbxXml.Parse(xml, unprotect);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(compositeKey);
            CryptographicOperations.ZeroMemory(transformedKey);
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(hmacBaseKey);
        }
    }

    private static byte[] DecryptContent(KdbxHeader header, byte[] masterKey, byte[] ciphertext)
    {
        if (KdbxFormat.UuidEquals(header.CipherId, KdbxFormat.CipherAes256Cbc))
        {
            return KdbxCrypto.AesCbcDecrypt(masterKey, header.EncryptionIv, ciphertext);
        }

        if (KdbxFormat.UuidEquals(header.CipherId, KdbxFormat.CipherChaCha20))
        {
            // ChaCha20 content cipher: key = masterKey, 12-byte nonce = IV.
            return KdbxCrypto.ChaCha20(masterKey, header.EncryptionIv, ciphertext);
        }

        throw new KdbxFormatException("Unsupported KDBX cipher (only AES-256-CBC and ChaCha20 are supported).");
    }

    private static (KdbxFormat.InnerStreamAlgorithm, byte[]) ReadInnerHeader(KdbxReader reader)
    {
        var innerId = KdbxFormat.InnerStreamAlgorithm.ChaCha20;
        byte[] innerKey = [];

        while (true)
        {
            byte fieldId = reader.ReadByte();
            int length = reader.ReadInt32();
            byte[] data = reader.ReadBytes(length);

            var id = (KdbxFormat.InnerHeaderFieldId)fieldId;
            if (id == KdbxFormat.InnerHeaderFieldId.EndOfHeader)
            {
                break;
            }

            switch (id)
            {
                case KdbxFormat.InnerHeaderFieldId.InnerRandomStreamId:
                    innerId = (KdbxFormat.InnerStreamAlgorithm)BitConverter.ToUInt32(data, 0);
                    break;
                case KdbxFormat.InnerHeaderFieldId.InnerRandomStreamKey:
                    innerKey = data;
                    break;
                case KdbxFormat.InnerHeaderFieldId.Binary:
                    break; // attachments are not imported
                default:
                    break;
            }
        }

        return (innerId, innerKey);
    }
}
