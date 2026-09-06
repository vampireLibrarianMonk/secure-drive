using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>Shared low-level helpers for the vault crypto primitives.</summary>
internal static class VaultPrimitives
{
    public const int GcmTagSize = 16;

    public static byte[] CombineKeys(VaultKeys keys) => [.. keys.EncryptionKey.ToArray(), .. keys.MacKey.ToArray()];

    /// <summary>Chunk AAD per the vault format: chunk index (64-bit BE) || file header nonce.</summary>
    public static byte[] BuildChunkAad(long chunkIndex, ReadOnlySpan<byte> headerNonce)
    {
        byte[] aad = new byte[8 + headerNonce.Length];
        if (!BitConverter.TryWriteBytes(aad.AsSpan(0, 8), chunkIndex))
        {
            throw new InvalidOperationException("Unable to write chunk index.");
        }

        if (BitConverter.IsLittleEndian)
        {
            aad.AsSpan(0, 8).Reverse();
        }

        headerNonce.CopyTo(aad.AsSpan(8));
        return aad;
    }

    public static int ReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    public static void EncryptGcm(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Stream ciphertextTarget, Span<byte> tag, ReadOnlySpan<byte> aad = default)
    {
        byte[] ct = new byte[plaintext.Length];
        using var gcm = new AesGcm(key.ToArray(), GcmTagSize);
        gcm.Encrypt(nonce, plaintext, ct, tag, aad);
        ciphertextTarget.Write(ct, 0, ct.Length);
    }

    public static void DecryptGcm(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, Stream plaintextTarget, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> aad = default)
    {
        byte[] pt = new byte[ciphertext.Length];
        using var gcm = new AesGcm(key.ToArray(), GcmTagSize);
        gcm.Decrypt(nonce, ciphertext, tag, pt, aad);
        plaintextTarget.Write(pt, 0, pt.Length);
    }
}
