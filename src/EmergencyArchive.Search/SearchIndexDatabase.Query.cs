using Microsoft.Data.Sqlite;

namespace EmergencyArchive.Search;

/// <summary>Query and enumeration half of the search index (see SearchIndexDatabase.cs).</summary>
public sealed partial class SearchIndexDatabase
{
    /// <summary>Reconstructs the FTS5 index from the document rows (external-content 'rebuild').</summary>
    public void RebuildFts()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Execute(connection, "INSERT INTO document_text(document_text) VALUES('rebuild');");
        ftsDirty = false;
    }

    /// <summary>
    /// Searches the index. The query must already be sanitized (see
    /// Fts5Query.Sanitize). Returns up to <paramref name="limit"/> results,
    /// best matches first, with an optional category filter (spec section 10).
    /// </summary>
    public IReadOnlyList<SearchResultItem> Search(string ftsQuery, string? category = null, int limit = 200)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ftsDirty)
        {
            RebuildFts();
        }

        var results = new List<SearchResultItem>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.relative_path, d.logical_name, d.category, d.size, d.modified_at,
                   snippet(document_text, 0, '[', ']', ' … ', 16)
            FROM document_text
            JOIN documents AS d ON d.id = document_text.rowid
            WHERE document_text MATCH @query
            """ + (category is null ? string.Empty : " AND d.category = @category") + " ORDER BY rank LIMIT @limit;";
        command.Parameters.AddWithValue("@query", ftsQuery);
        command.Parameters.AddWithValue("@limit", limit);
        if (category is not null)
        {
            command.Parameters.AddWithValue("@category", category);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SearchResultItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5)));
        }

        return results;
    }

    /// <summary>Returns every indexed document (used for persisting the index into the vault).</summary>
    public IReadOnlyList<IndexedDocument> GetAll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var documents = new List<IndexedDocument>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT relative_path, logical_name, category, mime_type, size, modified_at, indexed_at, sha256, body
            FROM documents ORDER BY relative_path;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            documents.Add(new IndexedDocument(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                DateTimeOffset.Parse(reader.GetString(5)),
                DateTimeOffset.Parse(reader.GetString(6)),
                reader.GetString(7),
                reader.GetString(8)));
        }

        return documents;
    }
}
