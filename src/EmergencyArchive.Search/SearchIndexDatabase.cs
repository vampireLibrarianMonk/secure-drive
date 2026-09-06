using Microsoft.Data.Sqlite;

namespace EmergencyArchive.Search;

/// <summary>
/// The FTS5 search index, held in an in-memory SQLite database while the vault
/// is unlocked (spec section 9: the plaintext index never touches disk).
/// Persisted form: the document rows are serialized into the vault as an
/// encrypted blob; the FTS5 index itself is rebuilt from those rows on load.
/// </summary>
public sealed partial class SearchIndexDatabase : IDisposable
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS documents (
            id INTEGER PRIMARY KEY,
            relative_path TEXT NOT NULL UNIQUE,
            logical_name TEXT NOT NULL,
            category TEXT NOT NULL,
            mime_type TEXT NOT NULL,
            size INTEGER NOT NULL,
            modified_at TEXT NOT NULL,
            indexed_at TEXT NOT NULL,
            sha256 TEXT NOT NULL,
            body TEXT NOT NULL
        );
        CREATE VIRTUAL TABLE IF NOT EXISTS document_text USING fts5(body, content='documents', content_rowid='id');
        """;

    private readonly SqliteConnection connection;
    private bool ftsDirty = true;
    private bool disposed;

    private SearchIndexDatabase(SqliteConnection connection)
    {
        this.connection = connection;
    }

    public static SearchIndexDatabase OpenEmpty()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var database = new SearchIndexDatabase(connection);
        Execute(connection, SchemaSql);
        return database;
    }

    /// <summary>Inserts or updates one document. The FTS index is refreshed lazily before searching.</summary>
    public void Upsert(IndexedDocument document)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Execute(connection, """
            INSERT INTO documents(relative_path, logical_name, category, mime_type, size, modified_at, indexed_at, sha256, body)
            VALUES (@path, @name, @category, @mime, @size, @modified, @indexed, @sha, @body)
            ON CONFLICT(relative_path) DO UPDATE SET
                logical_name = @name, category = @category, mime_type = @mime, size = @size,
                modified_at = @modified, indexed_at = @indexed, sha256 = @sha, body = @body
            """,
            ("path", document.RelativePath), ("name", document.LogicalName), ("category", document.Category),
            ("mime", document.MimeType), ("size", document.Size), ("modified", document.ModifiedAt.ToString("O")),
            ("indexed", document.IndexedAt.ToString("O")), ("sha", document.Sha256), ("body", document.Body));
        ftsDirty = true;
    }

    /// <summary>Removes a document from the index. The FTS index is refreshed lazily before searching.</summary>
    public void Remove(string relativePath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Execute(connection, "DELETE FROM documents WHERE relative_path = @path;", ("path", relativePath));
        ftsDirty = true;
    }

    public int DocumentCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM documents;";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            connection.Dispose();
        }
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
