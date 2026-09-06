using System.IO;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;

namespace EmergencyArchive.Sync;

/// <summary>One replica of the archive: a vault plus its committed manifest state (spec section 17).</summary>
public sealed record ReplicaInfo(
    string VaultPath,
    string ArchiveId,
    string ArchiveVersion,
    long Revision,
    string ContentHash,
    DateTimeOffset? LastUpdateUtc,
    int DocumentCount,
    long TotalLogicalBytes,
    bool ManifestPresent)
{
    /// <summary>True when this replica has at least one committed archive revision.</summary>
    public bool IsCommitted => ManifestPresent && DocumentCount > 0;
}

public enum ReplicaSyncState
{
    /// <summary>Both replicas have the same archive ID, revision, and content hash.</summary>
    InSync,

    /// <summary>The first replica carries a newer committed revision.</summary>
    FirstIsNewer,

    /// <summary>The second replica carries a newer committed revision.</summary>
    SecondIsNewer,

    /// <summary>Same archive and revision but different content hashes — the replicas diverged.</summary>
    Diverged,

    /// <summary>The two vaults do not belong to the same archive (different IDs).</summary>
    DifferentArchives,

    /// <summary>At least one replica has never committed an archive revision.</summary>
    Uncommitted,
}

public sealed record ReplicaComparison(ReplicaInfo First, ReplicaInfo Second, ReplicaSyncState State, string Summary);

/// <summary>
/// Inspects and compares replicas of the archive across USB drives (spec
/// section 17). Every replica is independently encrypted; comparison happens
/// on manifest metadata only — no document content is decrypted for this.
/// </summary>
public static class ReplicaInspector
{
    /// <summary>Inspects one unlocked vault into a <see cref="ReplicaInfo"/>.</summary>
    public static ReplicaInfo Inspect(VaultSession session, string vaultPath)
    {
        ArchiveManifest? manifest = ManifestStore.Load(session);
        if (manifest is null)
        {
            return new ReplicaInfo(
                VaultPath: Path.GetFullPath(vaultPath),
                ArchiveId: session.VaultId,
                ArchiveVersion: "not committed",
                Revision: 0,
                ContentHash: string.Empty,
                LastUpdateUtc: null,
                DocumentCount: 0,
                TotalLogicalBytes: 0,
                ManifestPresent: false);
        }

        return new ReplicaInfo(
            VaultPath: Path.GetFullPath(vaultPath),
            ArchiveId: manifest.ArchiveId,
            ArchiveVersion: manifest.ArchiveVersion,
            Revision: manifest.Revision,
            ContentHash: manifest.ContentHash,
            LastUpdateUtc: manifest.CreatedUtc,
            DocumentCount: manifest.DocumentCount,
            TotalLogicalBytes: manifest.TotalLogicalBytes,
            ManifestPresent: true);
    }

    /// <summary>Compares two replicas of the archive (order: first, then second).</summary>
    public static ReplicaComparison Compare(ReplicaInfo first, ReplicaInfo second)
    {
        if (!first.ManifestPresent && !second.ManifestPresent)
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.Uncommitted, "Neither replica has a committed archive revision.");
        }

        if (!first.ManifestPresent)
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.Uncommitted, $"'{second.VaultPath}' has revision {second.Revision}; the other replica has never been committed.");
        }

        if (!second.ManifestPresent)
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.Uncommitted, $"'{first.VaultPath}' has revision {first.Revision}; the other replica has never been committed.");
        }

        if (!string.Equals(first.ArchiveId, second.ArchiveId, StringComparison.OrdinalIgnoreCase))
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.DifferentArchives, "The two vaults belong to different archives (different archive IDs).");
        }

        if (first.Revision != second.Revision)
        {
            bool firstNewer = first.Revision > second.Revision;
            string newerPath = firstNewer ? first.VaultPath : second.VaultPath;
            string state = firstNewer ? nameof(ReplicaSyncState.FirstIsNewer) : nameof(ReplicaSyncState.SecondIsNewer);
            return new ReplicaComparison(first, second, firstNewer ? ReplicaSyncState.FirstIsNewer : ReplicaSyncState.SecondIsNewer,
                $"Same archive: '{newerPath}' is newer (revision {Math.Max(first.Revision, second.Revision)} vs {Math.Min(first.Revision, second.Revision)}).");
        }

        if (string.Equals(first.ContentHash, second.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.InSync, $"Replicas are in sync at revision {first.Revision} ({first.DocumentCount} documents each).");
        }

        return new ReplicaComparison(first, second, ReplicaSyncState.Diverged, $"Replicas DIVERGED at revision {first.Revision}: same version, different content. Re-run the update on both from the master sources.");
    }
}
