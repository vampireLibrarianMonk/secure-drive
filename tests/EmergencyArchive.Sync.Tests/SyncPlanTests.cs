using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

public class SyncPlanTests
{
    [Fact]
    public void Empty_HasNoChanges()
    {
        Assert.True(SyncPlan.Empty.IsEmpty);
        Assert.Equal(0, SyncPlan.Empty.TotalChanges);
    }

    [Fact]
    public void TotalChanges_SumsAllKinds()
    {
        SyncPlan plan = new(
            Added: ["a.pdf", "b.pdf"],
            Changed: ["c.docx"],
            Deleted: ["d.tmp"]);

        Assert.False(plan.IsEmpty);
        Assert.Equal(4, plan.TotalChanges);
    }

    [Fact]
    public void EnumerateChanges_YieldsAllChangesWithKind()
    {
        SyncPlan plan = new(
            Added: ["new.pdf"],
            Changed: ["old.docx"],
            Deleted: ["gone.txt"]);

        List<(ChangeKind Kind, string RelativePath)> changes = [.. plan.EnumerateChanges()];

        Assert.Equal(3, changes.Count);
        Assert.Contains((ChangeKind.Added, "new.pdf"), changes);
        Assert.Contains((ChangeKind.Changed, "old.docx"), changes);
        Assert.Contains((ChangeKind.Deleted, "gone.txt"), changes);
    }

    [Fact]
    public void EmptyPlan_MatchesExplicitEmptyConstruction()
    {
        SyncPlan plan = new([], [], []);

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.EnumerateChanges());
    }
}
