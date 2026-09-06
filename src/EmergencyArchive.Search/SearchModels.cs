namespace EmergencyArchive.Search;

/// <summary>
/// One document in the search index, with its metadata and extracted text
/// (spec section 8). The extracted text includes the file name and relative
/// path so name searches match too.
/// </summary>
public sealed record IndexedDocument(
    string RelativePath,
    string LogicalName,
    string Category,
    string MimeType,
    long Size,
    DateTimeOffset ModifiedAt,
    DateTimeOffset IndexedAt,
    string Sha256,
    string Body);

/// <summary>One search result as presented to the user interface.</summary>
public sealed record SearchResultItem(
    string RelativePath,
    string LogicalName,
    string Category,
    long Size,
    DateTimeOffset ModifiedAt,
    string Snippet);

/// <summary>Progress report during index building (spec section 8).</summary>
public sealed record SearchIndexProgress(int Processed, int Total, string CurrentName);

/// <summary>How the in-memory search index came to be.</summary>
public enum SearchIndexStatus
{
    /// <summary>The encrypted index file was found in the vault and loaded.</summary>
    Loaded,

    /// <summary>No index existed; it was built from the stored documents.</summary>
    Built,

    /// <summary>The index file was damaged; the index was rebuilt from the documents (spec section 24: the index is disposable).</summary>
    RebuiltAfterCorruption,
}
