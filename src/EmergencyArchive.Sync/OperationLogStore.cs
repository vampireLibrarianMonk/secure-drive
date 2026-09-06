using System.IO;
using System.Text.Json;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;

namespace EmergencyArchive.Sync;

/// <summary>
/// Persists the operational log (spec section 20) as an encrypted file inside
/// the vault: archive metadata may be logged, but it must reside inside
/// encrypted storage.
/// </summary>
public static class OperationLogStore
{
    public const string LogPath = VaultPaths.LogsPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Loads the log from the vault; returns an empty log when none exists.</summary>
    public static OperationLog Load(VaultSession session)
    {
        if (!session.FileExists(LogPath))
        {
            return new OperationLog();
        }

        var entries = JsonSerializer.Deserialize<List<OperationLogEntry>>(session.ReadFile(LogPath), JsonOptions) ?? [];
        var log = new OperationLog();
        foreach (OperationLogEntry entry in entries)
        {
            log.Append(entry.TimestampUtc, entry.Category, entry.Message);
        }

        return log;
    }

    public static void Save(VaultSession session, OperationLog log)
    {
        using var target = new MemoryStream();
        JsonSerializer.Serialize(target, log.Entries, JsonOptions);
        target.Position = 0;
        session.WriteFile(LogPath, target);
    }
}
