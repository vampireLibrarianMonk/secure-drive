namespace EmergencyArchive.Sync;

/// <summary>The kind of change detected for one file during an archive update (spec section 14).</summary>
public enum ChangeKind
{
    Added,
    Changed,
    Deleted,
}

/// <summary>
/// The outcome of comparing a source scan against the archive manifest:
/// the files to add, change, or delete in the encrypted archive.
/// </summary>
public sealed record SyncPlan(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Deleted)
{
    public static readonly SyncPlan Empty = new([], [], []);

    /// <summary>Total number of file-level changes in this plan.</summary>
    public int TotalChanges => Added.Count + Changed.Count + Deleted.Count;

    /// <summary>True when the sources and the manifest already agree.</summary>
    public bool IsEmpty => TotalChanges == 0;

    /// <summary>Enumerates all changes as (kind, relative path) pairs.</summary>
    public IEnumerable<(ChangeKind Kind, string RelativePath)> EnumerateChanges()
    {
        foreach (string path in Added)
        {
            yield return (ChangeKind.Added, path);
        }

        foreach (string path in Changed)
        {
            yield return (ChangeKind.Changed, path);
        }

        foreach (string path in Deleted)
        {
            yield return (ChangeKind.Deleted, path);
        }
    }
}
