namespace EmergencyArchive.Core;

/// <summary>
/// Metadata stored with every completed archive update (spec section 18):
/// archive identity, version, timestamps, format versions, and totals.
/// </summary>
public sealed record ArchiveInfo(
    string ArchiveId,
    ArchiveVersion Version,
    DateTimeOffset GeneratedAt,
    string ApplicationVersion,
    int SchemaVersion,
    string EncryptionFormatVersion,
    int DocumentCount,
    long TotalLogicalBytes)
{
    /// <summary>Friendly size summary such as "12.8 GB", used on the setup dashboard.</summary>
    public string TotalLogicalBytesDisplay =>
        TotalLogicalBytes switch
        {
            < 1024 => $"{TotalLogicalBytes} B",
            < 1024L * 1024 => $"{TotalLogicalBytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{TotalLogicalBytes / (1024.0 * 1024):F1} MB",
            < 1024L * 1024 * 1024 * 1024 => $"{TotalLogicalBytes / (1024.0 * 1024 * 1024):F1} GB",
            _ => $"{TotalLogicalBytes / (1024.0 * 1024 * 1024 * 1024):F1} TB",
        };
}
