using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// Payload-level helpers: the KDBX4 HMAC-authenticated block stream, GZip
/// (de)compression with an upper bound, and the inner protected-value stream
/// cipher used to hide passwords inside the XML.
/// </summary>
internal static class KdbxPayload
{
    /// <summary>Upper bound on decompressed payload (defensive; a KeePass DB is far smaller).</summary>
    private const int MaxDecompressedBytes = 128 * 1024 * 1024;

    /// <summary>
    /// Reads and authenticates the KDBX4 HMAC block stream that follows the
    /// header + header-HMAC. Each block is [hmac:32][length:int32][data]; the
    /// per-block HMAC-SHA256 key is derived from the block index. A final block
    /// of length 0 terminates the stream. Returns the concatenated ciphertext.
    /// </summary>
    public static byte[] ReadHmacBlockStream(KdbxReader reader, byte[] hmacBaseKey)
    {
        using var output = new MemoryStream();
        ulong blockIndex = 0;
        while (true)
        {
            byte[] storedHmac = reader.ReadBytes(32);
            int length = reader.ReadInt32();
            if (length < 0)
            {
                throw new KdbxFormatException("KDBX HMAC block has a negative length.");
            }

            byte[] blockData = reader.ReadBytes(length);

            byte[] blockKey = KdbxKeyDerivation.BlockHmacKey(blockIndex, hmacBaseKey);
            byte[] indexLe = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(indexLe, blockIndex);
            byte[] lengthLe = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(lengthLe, length);

            using (var hmac = new HMACSHA256(blockKey))
            {
                hmac.TransformBlock(indexLe, 0, indexLe.Length, null, 0);
                hmac.TransformBlock(lengthLe, 0, lengthLe.Length, null, 0);
                hmac.TransformFinalBlock(blockData, 0, blockData.Length);
                byte[] computed = hmac.Hash!;
                if (!CryptographicOperations.FixedTimeEquals(computed, storedHmac))
                {
                    throw new KdbxAuthenticationException();
                }
            }

            if (length == 0)
            {
                break; // terminating empty block
            }

            output.Write(blockData, 0, blockData.Length);
            blockIndex++;
        }

        return output.ToArray();
    }

    /// <summary>
    /// Writes a KDBX4 HMAC block stream for the given ciphertext (single block
    /// plus a terminating empty block), returning the framed bytes.
    /// </summary>
    public static byte[] WriteHmacBlockStream(byte[] ciphertext, byte[] hmacBaseKey)
    {
        var writer = new KdbxWriter();
        WriteHmacBlock(writer, 0, ciphertext, hmacBaseKey);
        WriteHmacBlock(writer, 1, [], hmacBaseKey);
        return writer.ToArray();
    }

    private static void WriteHmacBlock(KdbxWriter writer, ulong blockIndex, byte[] data, byte[] hmacBaseKey)
    {
        byte[] blockKey = KdbxKeyDerivation.BlockHmacKey(blockIndex, hmacBaseKey);
        byte[] indexLe = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(indexLe, blockIndex);
        byte[] lengthLe = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthLe, data.Length);

        using var hmac = new HMACSHA256(blockKey);
        hmac.TransformBlock(indexLe, 0, indexLe.Length, null, 0);
        hmac.TransformBlock(lengthLe, 0, lengthLe.Length, null, 0);
        hmac.TransformFinalBlock(data, 0, data.Length);

        writer.WriteBytes(hmac.Hash!);
        writer.WriteInt32(data.Length);
        writer.WriteBytes(data);
    }

    /// <summary>
    /// Reads the KDBX 3.1 hashed block stream: each block is
    /// [index:int32][hash:32][size:int32][data], verified with SHA-256(data);
    /// a block of size 0 terminates. Returns the concatenated data.
    /// </summary>
    public static byte[] ReadHashedBlockStream(KdbxReader reader)
    {
        using var output = new MemoryStream();
        int expectedIndex = 0;
        int total = 0;
        while (true)
        {
            int index = reader.ReadInt32();
            byte[] storedHash = reader.ReadBytes(32);
            int size = reader.ReadInt32();
            if (index != expectedIndex)
            {
                throw new KdbxFormatException("KDBX hashed block is out of order (file is corrupt).");
            }

            if (size < 0)
            {
                throw new KdbxFormatException("KDBX hashed block has a negative size.");
            }

            if (size == 0)
            {
                break; // terminating block
            }

            byte[] data = reader.ReadBytes(size);
            byte[] computed = SHA256.HashData(data);
            if (!CryptographicOperations.FixedTimeEquals(computed, storedHash))
            {
                throw new KdbxFormatException("KDBX hashed block failed its checksum (file is corrupt).");
            }

            total += size;
            if (total > MaxDecompressedBytes)
            {
                throw new KdbxFormatException("KDBX payload is unreasonably large.");
            }

            output.Write(data, 0, data.Length);
            expectedIndex++;
        }

        return output.ToArray();
    }

    public static byte[] GzipDecompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int total = 0;
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxDecompressedBytes)
            {
                throw new KdbxFormatException("KDBX payload is unreasonably large when decompressed.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    public static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Builds the inner protected-value stream cipher transform. Returns a
    /// function that XORs successive protected values with the keystream (the
    /// stream is stateful and consumed in document order). Salsa20 for KDBX3.1,
    /// ChaCha20 for KDBX4, per the documented KeePass derivation.
    /// </summary>
    public static Func<byte[], byte[]> CreateInnerStream(KdbxFormat.InnerStreamAlgorithm algorithm, byte[] protectedStreamKey)
    {
        switch (algorithm)
        {
            case KdbxFormat.InnerStreamAlgorithm.Salsa20:
                {
                    byte[] key = SHA256.HashData(protectedStreamKey);
                    var engine = new Salsa20Engine();
                    engine.Init(true, new ParametersWithIV(new KeyParameter(key), KdbxCrypto.KeePassSalsa20Iv));
                    return data => ProcessStream(engine, data);
                }

            case KdbxFormat.InnerStreamAlgorithm.ChaCha20:
                {
                    byte[] hash = SHA512.HashData(protectedStreamKey);
                    byte[] key = hash[..32];
                    byte[] nonce = hash[32..44];
                    var engine = new ChaCha7539Engine();
                    engine.Init(true, new ParametersWithIV(new KeyParameter(key), nonce));
                    return data => ProcessStream(engine, data);
                }

            default:
                throw new KdbxFormatException($"Unsupported inner random stream cipher ({algorithm}).");
        }
    }

    private static byte[] ProcessStream(IStreamCipher engine, byte[] data)
    {
        byte[] output = new byte[data.Length];
        engine.ProcessBytes(data, 0, data.Length, output, 0);
        return output;
    }
}
