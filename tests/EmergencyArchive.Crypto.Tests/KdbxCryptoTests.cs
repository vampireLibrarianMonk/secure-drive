using System.Security.Cryptography;
using EmergencyArchive.Crypto.Kdbx;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

/// <summary>
/// Known-answer and cross-implementation tests for the KDBX crypto primitives.
/// These pin correctness of the BouncyCastle wrappers before any file-format
/// code depends on them. ChaCha20 is cross-checked against an independent,
/// inline reference implementation of the RFC 7539 block function (so we do not
/// rely on a single library or a transcribed vector); Argon2 is checked against
/// the RFC 9106 reference vector; the rest use round-trip / inverse properties.
/// </summary>
public class KdbxCryptoTests
{
    // --- ChaCha20 (RFC 7539) cross-checked against an inline reference --------

    [Fact]
    public void ChaCha20_MatchesIndependentReferenceKeystream()
    {
        byte[] key = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            key[i] = (byte)i;
        }

        byte[] nonce = [0, 0, 0, 9, 0, 0, 0, 0x4a, 0, 0, 0, 0];

        // Encrypting zeros yields the raw keystream.
        byte[] zeros = new byte[256];
        byte[] keystream = KdbxCrypto.ChaCha20(key, nonce, zeros);

        // Independent reference: build the keystream from RFC 7539 blocks,
        // counter starting at 0 (matching ChaCha7539Engine's default).
        byte[] reference = ReferenceChaCha20Keystream(key, nonce, blockCount: 4);

        Assert.Equal(Convert.ToHexString(reference), Convert.ToHexString(keystream));
    }

    [Fact]
    public void ChaCha20_IsItsOwnInverse()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plaintext = RandomNumberGenerator.GetBytes(1000);

        byte[] ct = KdbxCrypto.ChaCha20(key, nonce, plaintext);
        Assert.NotEqual(Convert.ToHexString(plaintext), Convert.ToHexString(ct));

        byte[] pt = KdbxCrypto.ChaCha20(key, nonce, ct);
        Assert.Equal(plaintext, pt);
    }

    // --- Salsa20 --------------------------------------------------------------

    [Fact]
    public void Salsa20_IsItsOwnInverse_WithKeePassIv()
    {
        byte[] key = SHA256.HashData([1, 2, 3, 4]);
        byte[] plaintext = RandomNumberGenerator.GetBytes(500);

        byte[] ct = KdbxCrypto.Salsa20(key, KdbxCrypto.KeePassSalsa20Iv, plaintext);
        Assert.NotEqual(Convert.ToHexString(plaintext), Convert.ToHexString(ct));

        byte[] pt = KdbxCrypto.Salsa20(key, KdbxCrypto.KeePassSalsa20Iv, ct);
        Assert.Equal(plaintext, pt);
    }

    // --- Argon2 (RFC 9106 reference vector) -----------------------------------

    // RFC 9106 test-vector inputs (section 5): password = 32x 0x01,
    // salt = 16x 0x02, secret = 8x 0x03, associated data = 12x 0x04,
    // memory 32 KiB, passes 3, parallelism 4, tag length 32, version 0x13.
    // The expected tags are BouncyCastle's own asserted RFC 9106 conformance
    // values (see bc-csharp Argon2Test.TestVectorsFromSpecs) — these pin that
    // our wrapper invokes BC's Argon2 exactly as its conformance suite does.

    [Fact]
    public void Argon2id_MatchesReferenceVector()
    {
        byte[] tag = KdbxCrypto.Argon2(
            KdbxCrypto.Argon2Type.Argon2id,
            password: Fill(32, 0x01),
            salt: Fill(16, 0x02),
            iterations: 3,
            memoryKib: 32,
            parallelism: 4,
            outputLength: 32,
            secret: Fill(8, 0x03),
            associatedData: Fill(12, 0x04));

        Assert.Equal(
            "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659",
            Convert.ToHexString(tag).ToLowerInvariant());
    }

    [Fact]
    public void Argon2d_MatchesReferenceVector()
    {
        byte[] tag = KdbxCrypto.Argon2(
            KdbxCrypto.Argon2Type.Argon2d,
            password: Fill(32, 0x01),
            salt: Fill(16, 0x02),
            iterations: 3,
            memoryKib: 32,
            parallelism: 4,
            outputLength: 32,
            secret: Fill(8, 0x03),
            associatedData: Fill(12, 0x04));

        Assert.Equal(
            "512b391b6f1162975371d30919734294f868e3be3984f3c1a13a4db9fabe4acb",
            Convert.ToHexString(tag).ToLowerInvariant());
    }

    // --- AES-KDF (KDBX 3.1) ---------------------------------------------------

    [Fact]
    public void AesKdf_IsDeterministic_AndSeedSensitive()
    {
        byte[] composite = SHA256.HashData([9, 9, 9]);
        byte[] seed = Fill(32, 0x2a);

        byte[] a = KdbxCrypto.AesKdfTransform(composite, seed, rounds: 1000);
        byte[] b = KdbxCrypto.AesKdfTransform(composite, seed, rounds: 1000);
        Assert.Equal(a, b);
        Assert.Equal(32, a.Length);

        byte[] otherSeed = Fill(32, 0x2b);
        byte[] c = KdbxCrypto.AesKdfTransform(composite, otherSeed, rounds: 1000);
        Assert.NotEqual(Convert.ToHexString(a), Convert.ToHexString(c));

        byte[] moreRounds = KdbxCrypto.AesKdfTransform(composite, seed, rounds: 1001);
        Assert.NotEqual(Convert.ToHexString(a), Convert.ToHexString(moreRounds));
    }

    // --- AES-256-CBC ----------------------------------------------------------

    [Fact]
    public void AesCbc_RoundTrips()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] plaintext = RandomNumberGenerator.GetBytes(200);

        byte[] ct = KdbxCrypto.AesCbcEncrypt(key, iv, plaintext);
        byte[] pt = KdbxCrypto.AesCbcDecrypt(key, iv, ct);

        Assert.Equal(plaintext, pt);
    }

    // --- helpers --------------------------------------------------------------

    private static byte[] Fill(int length, byte value)
    {
        byte[] b = new byte[length];
        Array.Fill(b, value);
        return b;
    }

    /// <summary>
    /// A self-contained RFC 7539 ChaCha20 keystream generator used only to
    /// cross-check the BouncyCastle wrapper. Counter starts at 0.
    /// </summary>
    private static byte[] ReferenceChaCha20Keystream(byte[] key, byte[] nonce, int blockCount)
    {
        byte[] output = new byte[blockCount * 64];
        for (uint counter = 0; counter < blockCount; counter++)
        {
            byte[] block = ChaChaBlock(key, nonce, counter);
            Array.Copy(block, 0, output, (int)counter * 64, 64);
        }

        return output;
    }

    private static byte[] ChaChaBlock(byte[] key, byte[] nonce, uint counter)
    {
        uint[] state = new uint[16];
        state[0] = 0x61707865;
        state[1] = 0x3320646e;
        state[2] = 0x79622d32;
        state[3] = 0x6b206574;
        for (int i = 0; i < 8; i++)
        {
            state[4 + i] = BitConverter.ToUInt32(key, i * 4);
        }

        state[12] = counter;
        state[13] = BitConverter.ToUInt32(nonce, 0);
        state[14] = BitConverter.ToUInt32(nonce, 4);
        state[15] = BitConverter.ToUInt32(nonce, 8);

        uint[] working = (uint[])state.Clone();
        for (int i = 0; i < 10; i++)
        {
            QuarterRound(working, 0, 4, 8, 12);
            QuarterRound(working, 1, 5, 9, 13);
            QuarterRound(working, 2, 6, 10, 14);
            QuarterRound(working, 3, 7, 11, 15);
            QuarterRound(working, 0, 5, 10, 15);
            QuarterRound(working, 1, 6, 11, 12);
            QuarterRound(working, 2, 7, 8, 13);
            QuarterRound(working, 3, 4, 9, 14);
        }

        byte[] output = new byte[64];
        for (int i = 0; i < 16; i++)
        {
            uint sum = working[i] + state[i];
            BitConverter.GetBytes(sum).CopyTo(output, i * 4);
        }

        return output;
    }

    private static void QuarterRound(uint[] s, int a, int b, int c, int d)
    {
        s[a] += s[b]; s[d] = Rotl(s[d] ^ s[a], 16);
        s[c] += s[d]; s[b] = Rotl(s[b] ^ s[c], 12);
        s[a] += s[b]; s[d] = Rotl(s[d] ^ s[a], 8);
        s[c] += s[d]; s[b] = Rotl(s[b] ^ s[c], 7);
    }

    private static uint Rotl(uint x, int n) => (x << n) | (x >> (32 - n));
}
