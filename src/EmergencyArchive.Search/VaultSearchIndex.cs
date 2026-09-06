using System.Text.Json;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;

namespace EmergencyArchive.Search;

/// <summary>
/// Owns the lifecycle of the search index for one unlocked vault session:
/// load the encrypted index stored inside the vault (spec section 9) or build
/// it from the stored documents, answer full-text queries, and persist it back
/// as an encrypted file. The index is disposable (spec section 24): a damaged
/// index is rebuilt from the documents, which are authoritative.
/// </summary>
public sealed partial class VaultSearchIndex : IDisposable
{
    public const string IndexPath = VaultPaths.IndexPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SearchIndexDatabase database;
    private bool disposed;

    private VaultSearchIndex(SearchIndexDatabase database, SearchIndexStatus status)
    {
        this.database = database;
        Status = status;
    }

    public SearchIndexStatus Status { get; private set; }

    public int DocumentCount => database.DocumentCount;

    /// <summary>Loads the encrypted index from the vault, or builds it when absent or damaged.</summary>
    public static VaultSearchIndex LoadOrBuild(VaultSession session, IProgress<SearchIndexProgress>? progress = null)
    {
        if (session.FileExists(IndexPath))
        {
            try
            {
                return new VaultSearchIndex(Import(session), SearchIndexStatus.Loaded);
            }
            catch (Exception e) when (e is VaultException or JsonException)
            {
                // Spec section 24: a damaged index never blocks access to the
                // documents — rebuild from scratch and report the corruption.
                VaultSearchIndex rebuilt = Build(session, progress);
                rebuilt.Status = SearchIndexStatus.RebuiltAfterCorruption;
                return rebuilt;
            }
        }

        return Build(session, progress);
    }

    private static SearchIndexDatabase Import(VaultSession session)
    {
        byte[] bytes = session.ReadFile(IndexPath);
        List<IndexedDocument>? documents = JsonSerializer.Deserialize<List<IndexedDocument>>(bytes, JsonOptions);
        var database = SearchIndexDatabase.OpenEmpty();
        foreach (IndexedDocument document in documents ?? [])
        {
            database.Upsert(document);
        }

        database.RebuildFts();
        return database;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            database.Dispose();
        }
    }
}
