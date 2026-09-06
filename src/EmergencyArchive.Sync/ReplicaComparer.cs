namespace EmergencyArchive.Sync;

/// <summary>Compares two replicas of the archive (spec section 17).</summary>
public static class ReplicaComparer
{
    public static ReplicaComparison Compare(ReplicaInfo first, ReplicaInfo second)
    {
        if (!first.ManifestPresent && !second.ManifestPresent)
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.Uncommitted, "Neither replica has a committed archive revision.");
        }

        if (!first.ManifestPresent)
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.Uncommitted, $"The second replica has revision {second.Revision}; the first has never been committed.");
        }

        if (!second.ManifestPresent)
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.Uncommitted, $"The first replica has revision {first.Revision}; the second has never been committed.");
        }

        if (!string.Equals(first.ArchiveId, second.ArchiveId, StringComparison.OrdinalIgnoreCase))
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.DifferentArchives, "The two vaults belong to different archives (different archive IDs).");
        }

        if (first.Revision != second.Revision)
        {
            bool firstNewer = first.Revision > second.Revision;
            string newerPath = firstNewer ? first.VaultPath : second.VaultPath;
            long newer = firstNewer ? first.Revision : second.Revision;
            long older = firstNewer ? second.Revision : first.Revision;
            var state = firstNewer ? ReplicaSyncState.FirstIsNewer : ReplicaSyncState.SecondIsNewer;
            return new ReplicaComparison(first, second, state, $"Same archive: '{newerPath}' is newer (revision {newer} vs {older}).");
        }

        if (string.Equals(first.ContentHash, second.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return new ReplicaComparison(first, second, ReplicaSyncState.InSync, $"Replicas are in sync at revision {first.Revision} ({first.DocumentCount} documents each).");
        }

        return new ReplicaComparison(first, second, ReplicaSyncState.Diverged, $"Replicas DIVERGED at revision {first.Revision}: same version, different content. Re-run the update on both from the master sources.");
    }
}
