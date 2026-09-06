using EmergencyArchive.Core;
using Xunit;

namespace EmergencyArchive.Core.Tests;

public class OperationLogTests
{
    [Fact]
    public void Append_RecordsCategoryAndMessage()
    {
        var log = new OperationLog();
        var timestamp = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        log.Append(timestamp, "Update", "Update committed: version 2026.09.06.001.");

        var entry = Assert.Single(log.Entries);
        Assert.Equal(timestamp, entry.TimestampUtc);
        Assert.Equal("Update", entry.Category);
        Assert.Equal("Update committed: version 2026.09.06.001.", entry.Message);
    }

    [Fact]
    public void Entries_AreOldestFirst_AndNewestFirst_Reverses()
    {
        var log = new OperationLog();
        log.Append("Vault", "first");
        log.Append("Vault", "second");
        log.Append("Vault", "third");

        Assert.Equal(["first", "second", "third"], [.. log.Entries.Select(e => e.Message)]);
        Assert.Equal(["third", "second", "first"], [.. log.NewestFirst.Select(e => e.Message)]);
    }

    [Fact]
    public void Append_TrimsToMaxEntries_KeepingNewest()
    {
        var log = new OperationLog();
        for (int i = 0; i < OperationLog.MaxEntries + 25; i++)
        {
            log.Append("Test", $"entry {i}");
        }

        Assert.Equal(OperationLog.MaxEntries, log.Entries.Count);
        Assert.Equal("entry 25", log.Entries[0].Message); // oldest kept
        Assert.Equal($"entry {OperationLog.MaxEntries + 24}", log.Entries[^1].Message); // newest
    }

    [Fact]
    public void Append_IsThreadSafe()
    {
        var log = new OperationLog();
        var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                log.Append("Test", $"thread {t} entry {i}");
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        // All 800 appends complete without corruption; the bounded log keeps
        // the newest MaxEntries (thread-safety is about integrity, not count).
        Assert.Equal(OperationLog.MaxEntries, log.Entries.Count);
    }
}
