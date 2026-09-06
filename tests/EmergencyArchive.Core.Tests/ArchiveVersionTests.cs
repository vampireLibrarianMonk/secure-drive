using EmergencyArchive.Core;
using Xunit;

namespace EmergencyArchive.Core.Tests;

public class ArchiveVersionTests
{
    [Fact]
    public void Create_FormatsDateAndSequence()
    {
        ArchiveVersion version = ArchiveVersion.Create(new DateTimeOffset(2026, 9, 4, 16, 38, 0, TimeSpan.Zero), 1);

        Assert.Equal("2026.09.04.001", version.ToString());
    }

    [Fact]
    public void Create_RejectsZeroSequence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArchiveVersion.Create(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero), 0));
    }

    [Theory]
    [InlineData("2026.09.04.001")]
    [InlineData("1999.12.31.999")]
    public void Parse_RoundTrips(string text)
    {
        Assert.True(ArchiveVersion.TryParse(text, out ArchiveVersion version));
        Assert.Equal(text, version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026.09.04")]
    [InlineData("2026.09.04.001.5")]
    [InlineData("2026.13.04.001")]
    [InlineData("2026.09.32.001")]
    [InlineData("2026.09.04.000")]
    [InlineData("abcd.09.04.001")]
    public void Parse_RejectsInvalidInput(string? text)
    {
        Assert.False(ArchiveVersion.TryParse(text, out _));
    }

    [Fact]
    public void CompareTo_OrdersByDateThenSequence()
    {
        ArchiveVersion earlier = ArchiveVersion.Create(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero), 1);
        ArchiveVersion laterSameDay = ArchiveVersion.Create(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero), 2);
        ArchiveVersion nextDay = ArchiveVersion.Create(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero), 1);

        Assert.True(earlier.CompareTo(laterSameDay) < 0);
        Assert.True(nextDay.CompareTo(earlier) > 0);
        Assert.True(laterSameDay.CompareTo(nextDay) < 0);
    }
}

public class DocumentCategoriesTests
{
    [Fact]
    public void Default_MatchesSpecSection10()
    {
        string[] expected =
        [
            "Identity",
            "Financial",
            "Insurance",
            "Property",
            "Legal",
            "Medical",
            "Education",
            "Emergency",
            "Family",
            "Other",
        ];

        Assert.Equal(expected, DocumentCategories.Default);
    }

    [Fact]
    public void Default_HasNoDuplicatesOrEmptyNames()
    {
        Assert.All(DocumentCategories.Default, category => Assert.False(string.IsNullOrWhiteSpace(category)));
        Assert.Equal(DocumentCategories.Default.Count, DocumentCategories.Default.Distinct().Count());
    }
}
