using System.IO;
using System.Text.Json;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;

namespace EmergencyArchive.Sync;

/// <summary>
/// Loads and persists the archive manifest (spec section 15) as an encrypted
/// file inside the vault. Written LAST during an update: the manifest is the
/// commit marker of a successful update (spec section 14).
/// </summary>
public static class ManifestStore
{
    public const string ManifestPath = VaultPaths.ManifestPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Loads the manifest, or null when the vault has no manifest yet.</summary>
    public static ArchiveManifest? Load(VaultSession session)
    {
        if (!session.FileExists(ManifestPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ArchiveManifest>(session.ReadFile(ManifestPath), JsonOptions);
    }

    public static void Save(VaultSession session, ArchiveManifest manifest)
    {
        using var target = new MemoryStream();
        JsonSerializer.Serialize(target, manifest, JsonOptions);
        target.Position = 0;
        session.WriteFile(ManifestPath, target);
    }
}

/// <summary>Progress report for a running archive update.</summary>
public sealed record ArchiveUpdateProgress(string Phase, int Processed, int Total, string CurrentItem);

/// <summary>The result of one archive update run (spec section 14).</summary>
public sealed record ArchiveUpdateReport(
    SyncPlan Plan,
    ArchiveManifest Manifest,
    string ArchiveVersion,
    int DocumentCount,
    long TotalLogicalBytes,
    TimeSpan Duration,
    bool NoChanges);

/// <summary>
/// The archive update engine (spec section 14): scan sources → diff against
/// the manifest → apply changes with staged (atomic per-file) writes → write
/// the new manifest LAST (the commit marker). An interrupted update leaves the
/// previous manifest intact and the next update re-applies changes
/// idempotently — the last known-good archive is never at risk.
/// </summary>
public static class ArchiveUpdater
{
    public static ArchiveUpdateReport Update(
        VaultSession session,
        ArchiveManifest manifest,
        SourceConfiguration sources,
        IProgress<ArchiveUpdateProgress>? progress = null)
    {
        var started = DateTimeOffset.UtcNow;

        progress?.Report(new ArchiveUpdateProgress("Scanning sources", 0, 0, string.Empty));
        ScannedFileSet scan = SourceScanner.Scan(sources);

        // Diff the scan against the manifest (spec section 14).
        var manifestByPath = manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        var scannedByPath = scan.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

        List<string> added = [];
        List<string> changed = [];
        List<string> deleted = [];

        foreach (ScannedFile file in scan.Files)
        {
            if (!manifestByPath.TryGetValue(file.RelativePath, out ManifestEntry? entry))
            {
                added.Add(file.RelativePath);
            }
            else if (!string.Equals(entry.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase) || entry.Size != file.Size)
            {
                changed.Add(file.RelativePath);
            }
        }

        foreach (ManifestEntry entry in manifest.Entries)
        {
            if (!scannedByPath.ContainsKey(entry.RelativePath))
            {
                deleted.Add(entry.RelativePath);
            }
        }

        var plan = new SyncPlan(added, changed, deleted);

        if (plan.IsEmpty)
        {
            return new ArchiveUpdateReport(plan, manifest, manifest.ArchiveVersion, manifest.DocumentCount, manifest.TotalLogicalBytes, DateTimeOffset.UtcNow - started, NoChanges: true);
        }

        // Apply changes with staged writes (spec section 14).
        int done = 0;
        foreach ((ChangeKind kind, string path) in plan.EnumerateChanges())
        {
            progress?.Report(new ArchiveUpdateProgress("Applying changes", done, plan.TotalChanges, path));
            if (kind == ChangeKind.Deleted)
            {
                session.RemoveFile(path);
            }
            else
            {
                ScannedFile scanned = scan.Files.First(f => string.Equals(f.RelativePath, path, StringComparison.OrdinalIgnoreCase));
                using FileStream sourceStream = File.OpenRead(scanned.SourceFilePath);
                session.WriteFile(path, sourceStream);
            }

            done++;
        }

        // Commit: the new manifest is written LAST.
        List<ManifestEntry> entries = scan.Files
            .Select(f => new ManifestEntry(f.RelativePath, f.Size, f.ModifiedTimeUtc, f.Sha256))
            .ToList();

        // A manifest with no entries has never been committed: this is the
        // first revision (.001); otherwise the sequence increments same-day.
        string version = manifest.Entries.Count == 0
            ? ArchiveVersion.Create(DateTimeOffset.UtcNow, 1).ToString()
            : NextVersion(manifest.ArchiveVersion, DateTimeOffset.UtcNow);

        ArchiveManifest committed = manifest.WithEntries(entries, DateTimeOffset.UtcNow, version);
        ManifestStore.Save(session, committed);

        return new ArchiveUpdateReport(plan, committed, version, committed.DocumentCount, committed.TotalLogicalBytes, DateTimeOffset.UtcNow - started, NoChanges: false);
    }

    /// <summary>Archive version per spec section 18: YYYY.MM.DD.sequence (sequence resets daily).</summary>
    public static string NextVersion(string? previousVersion, DateTimeOffset now)
    {
        int sequence = 1;
        if (ArchiveVersion.TryParse(previousVersion, out ArchiveVersion previous)
            && previous.Year == now.Year && previous.Month == now.Month && previous.Day == now.Day)
        {
            sequence = previous.Sequence + 1;
        }

        return ArchiveVersion.Create(now, sequence).ToString();
    }
}
