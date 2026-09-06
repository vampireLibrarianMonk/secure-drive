using System.Security.Cryptography;
using System.Text;

namespace EmergencyArchive.Integrity;

/// <summary>
/// The integrity manifest of an archive revision (spec section 15): one entry
/// per stored document plus the archive identity and version. The manifest is
/// stored inside the encrypted vault (authenticated encrypted storage) and is
/// written LAST during an update — it is the commit marker.
/// </summary>
public sealed record ArchiveManifest(
    string ArchiveId,
    string ArchiveVersion,
    DateTimeOffset CreatedUtc,
    string ApplicationVersion,
    IReadOnlyList<ManifestEntry> Entries)
{
    public const string CurrentApplicationVersion = "0.1.0";

    /// <summary>
    /// Monotonic revision counter for replica comparison (spec section 17):
    /// 0 = never committed, 1 = first commit, +1 per update.
    /// </summary>
    public long Revision { get; init; } = 0;

    /// <summary>
    /// SHA-256 fingerprint of the sorted entry set — distinguishes replicas
    /// that share a version but have diverged in content (spec section 17).
    /// </summary>
    public string ContentHash => ContentHashFor(Entries);

    public static ArchiveManifest Empty(string archiveId, string archiveVersion) => new(
        archiveId,
        archiveVersion,
        DateTimeOffset.UtcNow,
        CurrentApplicationVersion,
        []);

    public ManifestEntry? Find(string relativePath)
    {
        return Entries.FirstOrDefault(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    public ArchiveManifest WithEntries(IReadOnlyList<ManifestEntry> entries, DateTimeOffset createdUtc, string archiveVersion)
    {
        return new ArchiveManifest(ArchiveId, archiveVersion, createdUtc, ApplicationVersion, entries)
        {
            Revision = Revision + 1,
        };
    }

    public long TotalLogicalBytes => Entries.Sum(e => e.Size);

    public int DocumentCount => Entries.Count;

    /// <summary>Order-independent content fingerprint: sorted entries, one line each.</summary>
    public static string ContentHashFor(IReadOnlyList<ManifestEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (ManifestEntry entry in entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            sb.Append(entry.RelativePath).Append('|').Append(entry.Size).Append('|').Append(entry.Sha256).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

/// <summary>One document entry of the manifest (spec section 15).</summary>
public sealed record ManifestEntry(
    string RelativePath,
    long Size,
    DateTimeOffset ModifiedTimeUtc,
    string Sha256);
