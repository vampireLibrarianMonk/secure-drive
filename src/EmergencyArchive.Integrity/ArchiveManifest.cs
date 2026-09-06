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
        return new ArchiveManifest(ArchiveId, archiveVersion, createdUtc, ApplicationVersion, entries);
    }

    public long TotalLogicalBytes => Entries.Sum(e => e.Size);

    public int DocumentCount => Entries.Count;
}

/// <summary>One document entry of the manifest (spec section 15).</summary>
public sealed record ManifestEntry(
    string RelativePath,
    long Size,
    DateTimeOffset ModifiedTimeUtc,
    string Sha256);
