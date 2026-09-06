using EmergencyArchive.Search;
using Xunit;

namespace EmergencyArchive.Search.Tests;

public class Fts5QueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sanitize_EmptyInputYieldsEmptyQuery(string? input)
    {
        Assert.Equal(string.Empty, Fts5Query.Sanitize(input));
    }

    [Fact]
    public void Sanitize_WrapsEachTermInPrefixQueries()
    {
        // Each term is a quoted prefix query: partial words match (spec §26).
        Assert.Equal("\"home\"* \"insurance\"*", Fts5Query.Sanitize("home insurance"));
    }

    [Fact]
    public void Sanitize_EscapesEmbeddedQuotes()
    {
        // "say \"hello\"" splits into two terms; the embedded quotes of the
        // second term are doubled inside its quoted phrase.
        Assert.Equal("\"say\"* \"\"\"hello\"\"\"*", Fts5Query.Sanitize("say \"hello\""));
    }

    [Fact]
    public void Sanitize_NeutralizesFts5Syntax()
    {
        // Operators and column filters must be treated as plain text.
        string result = Fts5Query.Sanitize("NEAR(a b) OR passport:*");

        Assert.Equal("\"NEAR(a\"* \"b)\"* \"OR\"* \"passport:*\"*", result);
    }

    [Fact]
    public void Sanitize_HandlesUnicode()
    {
        // CJK runs are segmented so every character is a searchable token.
        Assert.Equal("\"josé's\"* \"证 件\"*", Fts5Query.Sanitize("josé's 证件"));
    }

    [Fact]
    public void Sanitize_CollapsesWhitespace()
    {
        Assert.Equal("\"one\"* \"two\"*", Fts5Query.Sanitize("  one \t two  "));
    }
}
