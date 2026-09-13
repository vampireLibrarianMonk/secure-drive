using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// The cryptographic primitives KDBX (KeePass) needs, built on the same
/// BouncyCastle + .NET BCL foundation the vault uses (no GPL code). Each helper
/// is a thin, verifiable wrapper; correctness is pinned by known-answer vectors
/// in the test suite before any file-format code depends on it.
/// </summary>
internal static class KdbxCrypto
{
    /// <summary>Argon2 variant selector, matching the KDBX4 KDF UUIDs.</summary>
    internal enum Argon2Type
    {
        Argon2d,
        Argon2id,
    }

    /// <summary>
    /// Derives a key with Argon2 (KDBX4 KDF). <paramref name="memoryKib"/> is in
    /// KiB and <paramref name="iterations"/> is the time cost; both come from the
    /// file's KDF parameters. Version is Argon2 v1.3 (0x13), as KeePass writes.
    /// </summary>
    public static byte[] Argon2(
        Argon2Type type,
        byte[] password,
        byte[] salt,
        int iterations,
        long memoryKib,
        int parallelism,
        int outputLength,
        byte[]? secret = null,
        byte[]? associatedData = null)
    {
        int argonType = type == Argon2Type.Argon2id
            ? Org.BouncyCastle.Crypto.Parameters.Argon2Parameters.Argon2id
            : Org.BouncyCastle.Crypto.Parameters.Argon2Parameters.Argon2d;

        var builder = new Argon2Parameters.Builder(argonType)
            .WithVersion(Argon2Parameters.Version13)
            .WithSalt(salt)
            .WithIterations(iterations)
            .WithMemoryAsKB((int)memoryKib)
            .WithParallelism(parallelism);

        // KeePass uses neither a secret key nor associated data; these optional
        // inputs exist so the wrapper can reproduce the RFC 9106 test vectors.
        if (secret is { Length: > 0 })
        {
            builder = builder.WithSecret(secret);
        }

        if (associatedData is { Length: > 0 })
        {
            builder = builder.WithAdditional(associatedData);
        }

        Argon2Parameters parameters = builder.Build();

        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);
        byte[] output = new byte[outputLength];
        generator.GenerateBytes(password, output);
        return output;
    }

    /// <summary>
    /// The KDBX 3.1 "AES-KDF": encrypt the 32-byte composite key with AES-ECB
    /// using <paramref name="transformSeed"/> as the key, repeated
    /// <paramref name="rounds"/> times (both halves transformed in place), then
    /// SHA-256 the result. Implemented on BouncyCastle's raw AES engine, the
    /// same primitive the vault's AES-SIV uses.
    /// </summary>
    public static byte[] AesKdfTransform(byte[] compositeKey, byte[] transformSeed, ulong rounds)
    {
        if (compositeKey.Length != 32)
        {
            throw new ArgumentException("AES-KDF expects a 32-byte key.", nameof(compositeKey));
        }

        var engine = new AesEngine();
        engine.Init(forEncryption: true, new KeyParameter(transformSeed));

        // Transform the two 16-byte halves through AES-ECB for the given rounds.
        byte[] block = (byte[])compositeKey.Clone();
        byte[] tmp = new byte[16];
        for (ulong r = 0; r < rounds; r++)
        {
            engine.ProcessBlock(block, 0, tmp, 0);
            Array.Copy(tmp, 0, block, 0, 16);
            engine.ProcessBlock(block, 16, tmp, 0);
            Array.Copy(tmp, 0, block, 16, 16);
        }

        byte[] hash = SHA256.HashData(block);
        CryptographicOperations.ZeroMemory(block);
        CryptographicOperations.ZeroMemory(tmp);
        return hash;
    }

    /// <summary>
    /// XOR a buffer with the ChaCha20 keystream (RFC 7539: 256-bit key, 96-bit
    /// nonce, 32-bit block counter starting at 0). Used for the KDBX4 inner
    /// protected-stream and, with a bespoke setup, the ChaCha20 content cipher.
    /// </summary>
    public static byte[] ChaCha20(byte[] key, byte[] nonce, byte[] data)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("ChaCha20 expects a 32-byte key.", nameof(key));
        }

        if (nonce.Length != 12)
        {
            throw new ArgumentException("ChaCha20 (RFC 7539) expects a 12-byte nonce.", nameof(nonce));
        }

        var engine = new ChaCha7539Engine();
        engine.Init(forEncryption: true, new ParametersWithIV(new KeyParameter(key), nonce));
        byte[] output = new byte[data.Length];
        engine.ProcessBytes(data, 0, data.Length, output, 0);
        return output;
    }

    /// <summary>
    /// XOR a buffer with the Salsa20 keystream (KDBX 3.1 inner protected-stream).
    /// KeePass uses a fixed 8-byte IV and a 256-bit key derived as SHA-256 of the
    /// inner-random-stream key.
    /// </summary>
    public static byte[] Salsa20(byte[] key, byte[] iv, byte[] data)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("Salsa20 expects a 32-byte key.", nameof(key));
        }

        if (iv.Length != 8)
        {
            throw new ArgumentException("Salsa20 expects an 8-byte IV.", nameof(iv));
        }

        var engine = new Salsa20Engine();
        engine.Init(forEncryption: true, new ParametersWithIV(new KeyParameter(key), iv));
        byte[] output = new byte[data.Length];
        engine.ProcessBytes(data, 0, data.Length, output, 0);
        return output;
    }

    /// <summary>The fixed Salsa20 IV KeePass uses for the KDBX 3.1 inner stream.</summary>
    public static readonly byte[] KeePassSalsa20Iv = [0xE8, 0x30, 0x09, 0x4B, 0x97, 0x20, 0x5D, 0x2A];

    /// <summary>AES-256-CBC with PKCS7 padding (KDBX outer content cipher).</summary>
    public static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7);
    }

    /// <summary>AES-256-CBC with PKCS7 padding (KDBX outer content cipher).</summary>
    public static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
    }
}
