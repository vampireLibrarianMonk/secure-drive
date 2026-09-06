using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>
/// Loads and persists <c>masterkey.cryptomator</c> exactly as specified by the
/// Cryptomator vault format: a scrypt-derived 256-bit KEK wraps the two
/// 256-bit masterkeys with AES Key Wrap (RFC 3394); an HMAC over the legacy
/// version field prevents downgrade attacks.
/// </summary>
public static class VaultMasterkeyFile
{
    public const string FileName = "masterkey.cryptomator";

    /// <summary>Legacy field kept at Cryptomator's value; the vault version lives in vault.cryptomator.</summary>
    public const int LegacyVersion = 999;

    public const int DefaultScryptCostParam = 1 << 20; // N = 2^20
    public const int DefaultScryptBlockSize = 8;       // r = 8 (p = 1)

    private const int SaltLength = 8;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record MasterkeyFileDto(
        int Version,
        string ScryptSalt,
        int ScryptCostParam,
        int ScryptBlockSize,
        string PrimaryMasterKey,
        string HmacMasterKey,
        string VersionMac);

    /// <summary>Unlocks the vault keys. Throws <see cref="VaultUnlockException"/> for a wrong password.</summary>
    public static VaultKeys Unlock(string vaultRootPath, string password, byte[]? pepper = null)
    {
        string path = Path.Combine(vaultRootPath, FileName);
        if (!File.Exists(path))
        {
            throw new VaultFormatException($"Vault masterkey file is missing: {path}");
        }

        MasterkeyFileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<MasterkeyFileDto>(File.ReadAllText(path), JsonOptions)
                ?? throw new VaultFormatException("Masterkey file is empty.");
        }
        catch (JsonException e)
        {
            throw new VaultFormatException("Masterkey file is not valid JSON.", e);
        }

        if (dto.ScryptCostParam < 2 || dto.ScryptBlockSize < 1)
        {
            throw new VaultFormatException("Masterkey file has invalid scrypt parameters.");
        }

        byte[] kek = DeriveKek(password, Convert.FromBase64String(dto.ScryptSalt), pepper, dto.ScryptCostParam, dto.ScryptBlockSize);
        try
        {
            byte[] encKey = Unwrap(kek, Convert.FromBase64String(dto.PrimaryMasterKey));
            byte[] macKey = Unwrap(kek, Convert.FromBase64String(dto.HmacMasterKey));
            VerifyVersionMac(macKey, dto.Version, Convert.FromBase64String(dto.VersionMac));
            return new VaultKeys(encKey, macKey);
        }
        catch (Org.BouncyCastle.Crypto.InvalidCipherTextException)
        {
            // AES Key Wrap integrity check failed: the KEK was wrong.
            throw new VaultUnlockException();
        }
    }

    /// <summary>Writes a masterkey file protecting the given keys with the password.</summary>
    public static void Persist(string vaultRootPath, VaultKeys keys, string password, int scryptCostParam = DefaultScryptCostParam, int scryptBlockSize = DefaultScryptBlockSize, byte[]? pepper = null)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] kek = DeriveKek(password, salt, pepper, scryptCostParam, scryptBlockSize);

        var dto = new MasterkeyFileDto(
            LegacyVersion,
            Convert.ToBase64String(salt),
            scryptCostParam,
            scryptBlockSize,
            Convert.ToBase64String(Wrap(kek, keys.EncryptionKey.ToArray())),
            Convert.ToBase64String(Wrap(kek, keys.MacKey.ToArray())),
            Convert.ToBase64String(ComputeVersionMac(keys.MacKey.ToArray(), LegacyVersion)));

        string path = Path.Combine(vaultRootPath, FileName);
        string tempPath = path + ".staging";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(dto, JsonOptions));
        File.Move(tempPath, path, overwrite: true); // atomic: a partial write never destroys the vault
        CryptographicOperations.ZeroMemory(kek);
    }

    private static byte[] DeriveKek(string password, byte[] salt, byte[]? pepper, int costParam, int blockSize)
    {
        byte[] saltAndPepper = pepper is { Length: > 0 } ? [.. salt, .. pepper] : salt;
        byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        try
        {
            return SCrypt.Generate(passwordBytes, saltAndPepper, costParam, blockSize, 1, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static byte[] Wrap(byte[] kek, byte[] key)
    {
        var engine = new AesWrapEngine();
        engine.Init(true, new KeyParameter(kek));
        return engine.Wrap(key, 0, key.Length);
    }

    private static byte[] Unwrap(byte[] kek, byte[] wrapped)
    {
        var engine = new AesWrapEngine();
        engine.Init(false, new KeyParameter(kek));
        return engine.Unwrap(wrapped, 0, wrapped.Length);
    }

    internal static byte[] ComputeVersionMac(byte[] macKey, int version)
    {
        byte[] versionBytes = BitConverter.GetBytes(version);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(versionBytes); // big-endian 32-bit representation
        }

        using var hmac = new HMACSHA256(macKey);
        return hmac.ComputeHash(versionBytes);
    }

    private static void VerifyVersionMac(byte[] macKey, int version, byte[] expected)
    {
        byte[] actual = ComputeVersionMac(macKey, version);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new VaultFormatException("Vault masterkey file failed its downgrade protection check (versionMac).");
        }
    }
}
