using System.IO;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;

namespace EmergencyArchive.Sync;

/// <summary>
/// Snapshot for the Setup Mode dashboard (spec section 12): archive identity,
/// version, document count, size, drive free space, and last update time.
/// </summary>
public sealed record ArchiveDashboardSnapshot(
    string ArchiveId,
    string ArchiveVersion,
    long Revision,
    int DocumentCount,
    long TotalLogicalBytes,
    long DriveFreeBytes,
    DateTimeOffset? LastUpdateUtc,
    int SourceCount)
{
    public string ArchiveSizeDisplay => FormatBytes(TotalLogicalBytes);

    public string DriveFreeDisplay => FormatBytes(DriveFreeBytes);

    public string LastUpdateDisplay => LastUpdateUtc?.ToString("yyyy-MM-dd HH:mm") ?? "Never";

    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024L * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            < 1024L * 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
            _ => $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB",
        };
    }
}

/// <summary>Builds dashboard snapshots for Setup Mode (spec section 12).</summary>
public static class ArchiveDashboard
{
    public static ArchiveDashboardSnapshot Build(string vaultRootPath, ArchiveManifest? manifest, SourceConfiguration sources)
    {
        string fullVaultPath = Path.GetFullPath(vaultRootPath);
        string root = Path.GetPathRoot(fullVaultPath) ?? fullVaultPath;
        long freeBytes = new DriveInfo(root).AvailableFreeSpace;

        return new ArchiveDashboardSnapshot(
            ArchiveId: manifest?.ArchiveId ?? "EmergencyArchive",
            ArchiveVersion: manifest?.ArchiveVersion ?? "not committed",
            Revision: manifest?.Revision ?? 0,
            DocumentCount: manifest?.DocumentCount ?? 0,
            TotalLogicalBytes: manifest?.TotalLogicalBytes ?? 0,
            DriveFreeBytes: freeBytes,
            LastUpdateUtc: manifest?.CreatedUtc,
            SourceCount: sources.Sources.Count);
    }
}
