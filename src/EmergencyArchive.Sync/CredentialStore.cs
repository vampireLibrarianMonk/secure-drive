using System.IO;
using System.Text.Json;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;

namespace EmergencyArchive.Sync;

/// <summary>
/// Persists the credential database as an encrypted file inside the vault
/// (spec section 9 storage; excluded from document browse/search via
/// <see cref="VaultPaths.CredentialsPath"/>). Mirrors <see cref="SourceConfigStore"/>:
/// the vault layer provides the encryption, so this only serialises the model.
/// </summary>
public static class CredentialStore
{
    public const string StorePath = VaultPaths.CredentialsPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Loads the credential database, or an empty one when none exists yet.</summary>
    public static CredentialDatabase Load(VaultSession session)
    {
        if (!session.FileExists(StorePath))
        {
            return CredentialDatabase.Empty;
        }

        return JsonSerializer.Deserialize<CredentialDatabase>(session.ReadFile(StorePath), JsonOptions)
            ?? CredentialDatabase.Empty;
    }

    /// <summary>Writes the credential database into the vault (encrypted by the vault layer).</summary>
    public static void Save(VaultSession session, CredentialDatabase database)
    {
        using var target = new MemoryStream();
        JsonSerializer.Serialize(target, database, JsonOptions);
        target.Position = 0;
        session.WriteFile(StorePath, target);
    }
}
