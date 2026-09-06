using System.Security.Cryptography;
using System.Text.Json;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>
/// Creates and verifies <c>vault.cryptomator</c>: a JWT signed with the raw
/// 512-bit masterkey (HMAC-256) that binds the vault to format 8 / SIV_GCM and
/// protects the configuration against tampering and downgrade attacks.
/// </summary>
public static class VaultConfigFile
{
    public const string FileName = "vault.cryptomator";
    public const int VaultFormat = 8;
    public const string CipherCombo = "SIV_GCM";
    public const int DefaultShorteningThreshold = 220;
    public const string KeyId = "masterkeyfile:masterkey.cryptomator";

    public sealed record VaultConfiguration(string VaultId, int Format, string CipherCombo, int ShorteningThreshold);

    private sealed record HeaderDto(string Kid, string Typ, string Alg);

    private sealed record PayloadDto(string Jti, int Format, string CipherCombo, int ShorteningThreshold);

    public static void Persist(string vaultRootPath, VaultKeys keys, int shorteningThreshold = DefaultShorteningThreshold)
    {
        string vaultId = Guid.NewGuid().ToString();
        var header = new HeaderDto(KeyId, "JWT", "HS256");
        var payload = new PayloadDto(vaultId, VaultFormat, CipherCombo, shorteningThreshold);

        string headerSegment = VaultEncoding.EncodeJwtSegment(JsonSerializer.SerializeToUtf8Bytes(header));
        string payloadSegment = VaultEncoding.EncodeJwtSegment(JsonSerializer.SerializeToUtf8Bytes(payload));
        string signingInput = $"{headerSegment}.{payloadSegment}";

        byte[] rawKey = keys.RawMasterkey();
        using var hmac = new HMACSHA256(rawKey);
        byte[] signature = hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes(signingInput));

        File.WriteAllText(Path.Combine(vaultRootPath, FileName), $"{signingInput}.{VaultEncoding.EncodeJwtSegment(signature)}");
    }

    /// <summary>Verifies the signature and claims. Throws on tampering or unsupported configurations.</summary>
    public static VaultConfiguration LoadAndVerify(string vaultRootPath, VaultKeys keys)
    {
        string token = File.ReadAllText(Path.Combine(vaultRootPath, FileName)).Trim();
        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new VaultFormatException("vault.cryptomator is not a valid JWT.");
        }

        HeaderDto header;
        PayloadDto payload;
        try
        {
            header = JsonSerializer.Deserialize<HeaderDto>(VaultEncoding.DecodeJwtSegment(parts[0]))
                ?? throw new VaultFormatException("vault.cryptomator has an empty header.");
            payload = JsonSerializer.Deserialize<PayloadDto>(VaultEncoding.DecodeJwtSegment(parts[1]))
                ?? throw new VaultFormatException("vault.cryptomator has an empty payload.");
        }
        catch (JsonException e)
        {
            throw new VaultFormatException("vault.cryptomator is not valid JSON.", e);
        }

        if (header.Alg is not ("HS256" or "HS384" or "HS512"))
        {
            throw new VaultFormatException($"Unsupported vault config signature algorithm: {header.Alg}");
        }

        byte[] rawKey = keys.RawMasterkey();
        byte[] expected;
        using (HMACSHA256 hmac = new(rawKey))
        {
            expected = hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
        }

        byte[] actual = VaultEncoding.DecodeJwtSegment(parts[2]);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new VaultFormatException("vault.cryptomator signature verification failed: wrong password or tampered config.");
        }

        if (payload.Format != VaultFormat)
        {
            throw new VaultFormatException($"Unsupported vault format {payload.Format}; expected {VaultFormat}.");
        }

        if (!string.Equals(payload.CipherCombo, CipherCombo, StringComparison.Ordinal))
        {
            throw new VaultFormatException($"Unsupported cipher combination {payload.CipherCombo}; expected {CipherCombo}.");
        }

        if (!string.Equals(header.Kid, KeyId, StringComparison.Ordinal))
        {
            throw new VaultFormatException($"Unsupported key id {header.Kid}.");
        }

        return new VaultConfiguration(payload.Jti, payload.Format, payload.CipherCombo, payload.ShorteningThreshold);
    }
}
