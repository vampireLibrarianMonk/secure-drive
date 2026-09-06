using EmergencyArchive.Search;
using EmergencyArchive.Search.TextExtraction;
using Xunit;

namespace EmergencyArchive.Search.Tests;

public class SearchIndexDatabaseTests : IDisposable
{
    private readonly SearchIndexDatabase database = SearchIndexDatabase.OpenEmpty();

    private static IndexedDocument Document(string path, string body, string category = "Other") => new(
        path,
        Path.GetFileName(path),
        category,
        "application/pdf",
        1234,
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero),
        "abc123",
        body);

    public void Dispose() => database.Dispose();

    [Fact]
    public void Upsert_AddsAndCounts()
    {
        database.Upsert(Document("Birth Certificate.pdf", "Birth Certificate.pdf\nofficial text"));
        database.Upsert(Document("Passport.pdf", "Passport.pdf\npassport text"));

        Assert.Equal(2, database.DocumentCount);
    }

    [Fact]
    public void Upsert_SamePath_UpdatesInsteadOfDuplicating()
    {
        database.Upsert(Document("a.pdf", "first"));
        database.Upsert(Document("a.pdf", "second version"));

        Assert.Equal(1, database.DocumentCount);
        Assert.Equal("second version", database.GetAll().Single().Body);
    }

    [Fact]
    public void Search_ExactFilenameTerms_FindTheDocument()
    {
        database.Upsert(Document("Birth Certificate.pdf", "Birth Certificate.pdf\nregistry office"));

        var results = database.Search(Fts5Query.Sanitize("Birth Certificate"));

        Assert.Single(results);
        Assert.Equal("Birth Certificate.pdf", results[0].RelativePath);
    }

    [Fact]
    public void Search_PartialTerm_MatchesByPrefix()
    {
        database.Upsert(Document("Passport.pdf", "Passport.pdf\n Passport application"));

        var results = database.Search(Fts5Query.Sanitize("passp")); // partial word

        Assert.Single(results);
    }

    [Fact]
    public void Search_FindsContentWords()
    {
        database.Upsert(Document("2026/Policy.pdf", "2026/Policy.pdf\nThe home insurance policy covers fire and water damage."));

        var results = database.Search(Fts5Query.Sanitize("insurance fire"));

        Assert.Single(results);
    }

    [Fact]
    public void Search_Phrase_InQuotes_MatchesExactSequence()
    {
        database.Upsert(Document("a.pdf", "a.pdf\nhome insurance policy details"));
        database.Upsert(Document("b.pdf", "b.pdf\ninsurance policy for your home"));

        // A raw FTS5 phrase query (double quotes) matches the exact sequence...
        var phraseResults = database.Search("\"home insurance policy\"");

        Assert.Single(phraseResults);
        Assert.Equal("a.pdf", phraseResults[0].RelativePath);

        // ...and the same words in a different order do not.
        Assert.Empty(database.Search("\"policy home insurance\""));
    }

    [Fact]
    public void Search_NoResult_YieldsEmpty()
    {
        database.Upsert(Document("a.pdf", "a.pdf\ncompletely unrelated words"));

        Assert.Empty(database.Search(Fts5Query.Sanitize("zqjxkv")));
    }

    [Fact]
    public void Search_HandlesUnicode()
    {
        // The body is segmented exactly like VaultSearchIndex.ComposeBody does.
        string body = CjkText.Segment("证件/出生证明.pdf\n出生医学证明内容");
        database.Upsert(Document("证件/出生证明.pdf", body));

        var results = database.Search(Fts5Query.Sanitize("出生证明"));

        Assert.Single(results);
    }

    [Fact]
    public void Search_WithCategoryFilter_RestrictsResults()
    {
        database.Upsert(Document("Insurance/Home.pdf", "Insurance/Home.pdf\ninsurance documents and policies", category: "Insurance"));
        database.Upsert(Document("Identity/Passport.pdf", "Identity/Passport.pdf\ninsurance of identity documents", category: "Identity"));

        var insuranceOnly = database.Search(Fts5Query.Sanitize("insurance"), category: "Insurance");
        var identityOnly = database.Search(Fts5Query.Sanitize("insurance"), category: "Identity");

        Assert.Single(insuranceOnly);
        Assert.Equal("Insurance/Home.pdf", insuranceOnly[0].RelativePath);
        Assert.Single(identityOnly);
    }

    [Fact]
    public void GetAll_PreservesMetadata()
    {
        var original = Document("2026/Policy.pdf", "2026/Policy.pdf\nbody text", category: "2026");
        database.Upsert(original);

        IndexedDocument restored = database.GetAll().Single();

        Assert.Equal(original.RelativePath, restored.RelativePath);
        Assert.Equal(original.LogicalName, restored.LogicalName);
        Assert.Equal(original.Category, restored.Category);
        Assert.Equal(original.MimeType, restored.MimeType);
        Assert.Equal(original.Size, restored.Size);
        Assert.Equal(original.ModifiedAt, restored.ModifiedAt);
        Assert.Equal(original.IndexedAt, restored.IndexedAt);
        Assert.Equal(original.Sha256, restored.Sha256);
        Assert.Equal(original.Body, restored.Body);
    }

    [Fact]
    public void Search_AfterDispose_Throws()
    {
        database.Dispose();
        Assert.Throws<ObjectDisposedException>(() => database.Search("anything"));
    }
}
