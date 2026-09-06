using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>
/// AES-SIV per RFC 5297 with a 256-bit key (AEAD_AES_SIV_CMAC_256), the
/// deterministic mode used by the Cryptomator vault format for filenames and
/// directory IDs. Implemented verbatim from RFC 5297 on BouncyCastle's AES and
/// CMAC; conformance-tested against RFC 5297 appendix A.1 vectors.
/// The 256-bit key is split as K1 (S2V-CMAC) || K2 (AES-CTR).
/// </summary>
public static class Rfc5297Siv
{
    private const int BlockSize = 16;

    /// <summary>
    /// Encrypts plaintext; output = 16-byte synthetic IV || ciphertext.
    /// Key length: 32 bytes (CMAC_256), 48 (CMAC_384), or 64 (CMAC_512);
    /// the key splits into K1 (S2V-CMAC) and K2 (AES-CTR) of equal size.
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, params ReadOnlySpan<byte[]> associatedData)
    {
        if (key.Length is not (32 or 48 or 64))
        {
            throw new ArgumentException("AES-SIV requires a 32, 48, or 64 byte key.", nameof(key));
        }

        int half = key.Length / 2;
        byte[] v = new byte[BlockSize];
        S2V(key[..half], associatedData, plaintext, v);
        byte[] counter = PrepareCounter(v);
        byte[] ciphertext = Ctr(key[half..], counter, plaintext.ToArray());

        byte[] result = new byte[BlockSize + ciphertext.Length];
        v.AsSpan().CopyTo(result);
        ciphertext.AsSpan().CopyTo(result.AsSpan(BlockSize));
        return result;
    }

    /// <summary>
    /// Decrypts and authenticates. Returns false if the ciphertext or any
    /// associated data does not match (wrong key, wrong AD, or corruption).
    /// </summary>
    public static bool TryDecrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> ciphertext, out byte[] plaintext, params ReadOnlySpan<byte[]> associatedData)
    {
        plaintext = [];
        if (key.Length is not (32 or 48 or 64) || ciphertext.Length < BlockSize)
        {
            return false;
        }

        int half = key.Length / 2;
        Span<byte> v = stackalloc byte[BlockSize];
        ciphertext[..BlockSize].CopyTo(v);
        Span<byte> counter = stackalloc byte[BlockSize];
        ciphertext[..BlockSize].CopyTo(counter);
        counter[8] &= 0x7F;  // clear bit 63
        counter[12] &= 0x7F; // clear bit 31

        byte[] p = Ctr(key[half..], counter, ciphertext[BlockSize..].ToArray());

        Span<byte> expectedV = stackalloc byte[BlockSize];
        S2V(key[..half], associatedData, p, expectedV);

        if (!CryptographicOperations.FixedTimeEquals(v, expectedV))
        {
            CryptographicOperations.ZeroMemory(p);
            return false;
        }

        plaintext = p;
        return true;
    }

    /// <summary>S2V-CMAC-AES per RFC 5297 section 2.4.</summary>
    private static void S2V(ReadOnlySpan<byte> k1, ReadOnlySpan<byte[]> associatedData, ReadOnlySpan<byte> plaintext, Span<byte> output)
    {
        Span<byte> d = stackalloc byte[BlockSize];
        Cmac(k1, stackalloc byte[BlockSize], d); // D = CMAC(K1, <zero>)
        Span<byte> cmac = stackalloc byte[BlockSize];

        foreach (ReadOnlySpan<byte> ad in associatedData)
        {
            Dbl(d);
            Cmac(k1, ad, cmac);
            Xor(d, cmac);
        }

        if (plaintext.Length >= BlockSize)
        {
            // T = xorend(Sn, D): xor D onto the last 16 bytes, then final CMAC.
            byte[] xored = plaintext.ToArray();
            int offset = xored.Length - BlockSize;
            for (int i = 0; i < BlockSize; i++)
            {
                xored[offset + i] ^= d[i];
            }

            Cmac(k1, xored, output);
            return;
        }

        // T = dbl(D) xor pad(Sn), where pad(X) = X || 1 || 0...
        Dbl(d);
        Span<byte> t = stackalloc byte[BlockSize];
        t.Clear();
        plaintext.CopyTo(t);
        t[plaintext.Length] ^= 0x80;
        Xor(t, d);
        Cmac(k1, t, output);
    }

    /// <summary>Doubling in GF(2^128) per RFC 5297 section 2.3.</summary>
    private static void Dbl(Span<byte> block)
    {
        int carry = 0;
        for (int i = BlockSize - 1; i >= 0; i--)
        {
            int b = block[i];
            block[i] = (byte)((b << 1) | carry);
            carry = (b >> 7) & 1;
        }

        if (carry != 0)
        {
            block[BlockSize - 1] ^= 0x87;
        }
    }

    private static void Xor(Span<byte> target, ReadOnlySpan<byte> value)
    {
        for (int i = 0; i < target.Length; i++)
        {
            target[i] ^= value[i];
        }
    }

    private static void Cmac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, Span<byte> output)
    {
        var cmac = new CMac(new AesEngine());
        cmac.Init(new KeyParameter(key.ToArray()));
        byte[] buffer = data.ToArray();
        byte[] mac = new byte[BlockSize];
        cmac.BlockUpdate(buffer, 0, buffer.Length);
        cmac.DoFinal(mac, 0);
        mac.CopyTo(output);
    }

    /// <summary>AES-CTR per RFC 5297 section 2.5 (32-bit counter increment). Internal for test coverage.</summary>
    internal static byte[] Ctr(ReadOnlySpan<byte> k2, ReadOnlySpan<byte> counter, ReadOnlySpan<byte> data)
    {
        var aes = new AesEngine();
        aes.Init(true, new KeyParameter(k2.ToArray()));

        byte[] counterBlock = counter.ToArray();
        byte[] result = new byte[data.Length];
        byte[] keystream = new byte[BlockSize];

        for (int offset = 0; offset < data.Length; offset += BlockSize)
        {
            aes.ProcessBlock(counterBlock, 0, keystream, 0);
            int n = Math.Min(BlockSize, data.Length - offset);
            for (int i = 0; i < n; i++)
            {
                result[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
            }

            for (int i = BlockSize - 1; i >= BlockSize - 4; i--)
            {
                if (++counterBlock[i] != 0)
                {
                    break;
                }
            }
        }

        return result;
    }

    private static byte[] PrepareCounter(ReadOnlySpan<byte> v)
    {
        // RFC 5297 section 2.5: the 31st and 63rd bits of the counter (counted
        // from the rightmost bit = bit 0) are cleared before counter mode.
        byte[] counter = v.ToArray();
        counter[8] &= 0x7F;  // bit 63
        counter[12] &= 0x7F; // bit 31
        return counter;
    }
}
