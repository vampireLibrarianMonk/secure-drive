using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>
/// File header and content encryption per the vault format: a 68-byte header
/// protects a random per-file 256-bit content key; the content is split into
/// 32 KiB AES-GCM chunks whose AAD binds each chunk to its index and the header.
/// </summary>
public static class VaultContent
{
    public const int HeaderNonceSize = 12;
    public const int HeaderSize = 68;              // 12 nonce + 40 payload + 16 tag
    public const int ChunkPayloadSize = 32 * 1024; // 32 KiB
    public const int ChunkSize = HeaderNonceSize + ChunkPayloadSize + 16;

    /// <summary>Generates a fresh file header; returns (header, contentKey).</summary>
    public static (byte[] Header, byte[] ContentKey) EncryptHeader(VaultKeys keys)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(HeaderNonceSize);
        byte[] contentKey = RandomNumberGenerator.GetBytes(32);

        byte[] payload = new byte[40];
        Array.Fill(payload, (byte)0xFF, 0, 8); // reserved (formerly file size)
        contentKey.CopyTo(payload, 8);

        byte[] header = new byte[HeaderSize];
        nonce.CopyTo(header, 0);
        using (var gcm = new AesGcm(keys.EncryptionKey.ToArray(), VaultPrimitives.GcmTagSize))
        {
            gcm.Encrypt(nonce, payload, header.AsSpan(HeaderNonceSize, 40), header.AsSpan(HeaderNonceSize + 40));
        }

        return (header, contentKey);
    }

    /// <summary>Parses and authenticates a file header; returns the file's content key.</summary>
    public static byte[] DecryptHeader(VaultKeys keys, ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderSize)
        {
            throw new VaultIntegrityException($"File header has unexpected size {header.Length}.");
        }

        byte[] payload = new byte[40];
        try
        {
            using var gcm = new AesGcm(keys.EncryptionKey.ToArray(), VaultPrimitives.GcmTagSize);
            gcm.Decrypt(header[..HeaderNonceSize], header.Slice(HeaderNonceSize, 40), header.Slice(52, 16), payload);
        }
        catch (Exception e) when (e is AuthenticationTagMismatchException or CryptographicException)
        {
            throw new VaultIntegrityException("File header failed authentication.", e);
        }

        byte[] contentKey = payload[8..].ToArray();
        CryptographicOperations.ZeroMemory(payload);
        return contentKey;
    }

    /// <summary>Encrypts a plaintext stream into header + 32 KiB authenticated chunks.</summary>
    public static void EncryptStream(VaultKeys keys, Stream plaintext, Stream ciphertext)
    {
        (byte[] header, byte[] contentKey) = EncryptHeader(keys);
        ciphertext.Write(header, 0, header.Length);
        try
        {
            long chunkIndex = 0;
            byte[] buffer = new byte[ChunkPayloadSize];
            int read;
            while ((read = plaintext.Read(buffer, 0, buffer.Length)) > 0)
            {
                WriteChunk(contentKey, header.AsSpan(0, HeaderNonceSize), chunkIndex, buffer.AsSpan(0, read), ciphertext);
                chunkIndex++;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    /// <summary>Decrypts an encrypted file stream; every chunk is authenticated.</summary>
    public static void DecryptStream(VaultKeys keys, Stream ciphertext, Stream plaintext)
    {
        if (!ciphertext.CanSeek)
        {
            throw new ArgumentException("The ciphertext stream must be seekable.", nameof(ciphertext));
        }

        long cipherLength = ciphertext.Length;
        if (cipherLength < HeaderSize)
        {
            throw new VaultIntegrityException("File is truncated: missing or incomplete header.");
        }

        byte[] header = new byte[HeaderSize];
        int headerRead = VaultPrimitives.ReadExactly(ciphertext, header);
        if (headerRead < HeaderSize)
        {
            throw new VaultIntegrityException("File is truncated: missing or incomplete header.");
        }

        byte[] contentKey = DecryptHeader(keys, header);
        try
        {
            long contentLength = cipherLength - HeaderSize;
            if (contentLength == 0)
            {
                return; // empty file: header only
            }

            // The last chunk's payload size is derived from the total content length.
            long fullChunks = contentLength / ChunkSize;
            long remainder = contentLength % ChunkSize;
            long lastChunkPayload = remainder == 0
                ? ChunkPayloadSize
                : remainder - HeaderNonceSize - VaultPrimitives.GcmTagSize;
            if (lastChunkPayload <= 0 || lastChunkPayload > ChunkPayloadSize)
            {
                throw new VaultIntegrityException("File length does not correspond to whole encrypted chunks.");
            }

            long chunkIndex = 0;
            byte[] nonce = new byte[HeaderNonceSize];
            byte[] buffer = new byte[ChunkPayloadSize];
            byte[] tag = new byte[VaultPrimitives.GcmTagSize];

            while (chunkIndex < fullChunks)
            {
                ReadChunkInto(ciphertext, nonce, buffer, ChunkPayloadSize, tag);
                DecryptChunk(contentKey, nonce, buffer, ChunkPayloadSize, tag, chunkIndex, header, plaintext);
                chunkIndex++;
            }

            // Last (shorter) chunk, if any.
            if (remainder != 0 || fullChunks == 0)
            {
                ReadChunkInto(ciphertext, nonce, buffer, (int)lastChunkPayload, tag);
                DecryptChunk(contentKey, nonce, buffer, (int)lastChunkPayload, tag, chunkIndex, header, plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    private static void ReadChunkInto(Stream ciphertext, byte[] nonce, byte[] buffer, int payloadSize, byte[] tag)
    {
        if (VaultPrimitives.ReadExactly(ciphertext, nonce) < HeaderNonceSize)
        {
            throw new VaultIntegrityException("Truncated chunk nonce in file content.");
        }

        if (VaultPrimitives.ReadExactly(ciphertext, buffer.AsSpan(0, payloadSize)) < payloadSize)
        {
            throw new VaultIntegrityException("Truncated chunk payload in file content.");
        }

        if (VaultPrimitives.ReadExactly(ciphertext, tag) < tag.Length)
        {
            throw new VaultIntegrityException("Truncated chunk tag in file content.");
        }
    }

    private static void DecryptChunk(byte[] contentKey, byte[] nonce, byte[] buffer, int payloadSize, byte[] tag, long chunkIndex, byte[] header, Stream plaintext)
    {
        byte[] aad = VaultPrimitives.BuildChunkAad(chunkIndex, header.AsSpan(0, HeaderNonceSize));
        try
        {
            VaultPrimitives.DecryptGcm(contentKey, nonce, buffer.AsSpan(0, payloadSize), plaintext, tag, aad);
        }
        catch (Exception e) when (e is AuthenticationTagMismatchException or CryptographicException)
        {
            throw new VaultIntegrityException($"File content failed authentication in chunk {chunkIndex}.", e);
        }
    }

    private static void WriteChunk(byte[] contentKey, ReadOnlySpan<byte> headerNonce, long chunkIndex, ReadOnlySpan<byte> payload, Stream ciphertext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(HeaderNonceSize);
        byte[] tag = new byte[VaultPrimitives.GcmTagSize];
        byte[] aad = VaultPrimitives.BuildChunkAad(chunkIndex, headerNonce);

        ciphertext.Write(nonce, 0, nonce.Length);
        VaultPrimitives.EncryptGcm(contentKey, nonce, payload, ciphertext, tag, aad);
        ciphertext.Write(tag, 0, tag.Length);
    }
}
