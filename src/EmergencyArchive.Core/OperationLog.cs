namespace EmergencyArchive.Core;

/// <summary>One entry of the archive's operational log (spec section 20).</summary>
public sealed record OperationLogEntry(DateTimeOffset TimestampUtc, string Category, string Message);

/// <summary>
/// Bounded in-memory operational log (spec section 20): records what the
/// application did — never secrets. Entries are trimmed to
/// <see cref="MaxEntries"/> (newest kept) and persisted inside the encrypted
/// vault by the storage layer.
/// </summary>
/// <remarks>
/// Red lines enforced by convention at call sites (spec section 20): no
/// passwords, no key material, no extracted document text, and search queries
/// only in aggregated form (e.g. "Search performed: 3 results").
/// </remarks>
public sealed class OperationLog
{
    public const int MaxEntries = 500;

    private readonly List<OperationLogEntry> entries = [];
    private readonly object gate = new();

    /// <summary>All entries, oldest first.</summary>
    public IReadOnlyList<OperationLogEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return [.. entries];
            }
        }
    }

    /// <summary>Entries newest first (for activity views).</summary>
    public IReadOnlyList<OperationLogEntry> NewestFirst
    {
        get
        {
            lock (gate)
            {
                var copy = new List<OperationLogEntry>(entries);
                copy.Reverse();
                return copy;
            }
        }
    }

    public void Append(string category, string message)
    {
        Append(DateTimeOffset.UtcNow, category, message);
    }

    public void Append(DateTimeOffset timestampUtc, string category, string message)
    {
        var entry = new OperationLogEntry(timestampUtc, category, message);
        lock (gate)
        {
            entries.Add(entry);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(0, entries.Count - MaxEntries);
            }
        }
    }
}
