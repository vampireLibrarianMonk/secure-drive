using System.IO;
using System.Text.Json;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search.TextExtraction;

namespace EmergencyArchive.Search;

/// <summary>Index building, persistence, and query half of VaultSearchIndex.</summary>
public sealed partial class VaultSearchIndex
{
    /// <summary>Builds a fresh index from every stored document and persists it into the vault.</summary>
    public static VaultSearchIndex Build(VaultSession session, IProgress<SearchIndexProgress>? progress = null)
    {
        var database = SearchIndexDatabase.OpenEmpty();

        // Never index infrastructure files (index, manifest, source config).
        var files = session.EnumerateFiles()
            .Where(file => !VaultPaths.IsInfrastructurePath(file))
            .ToList();

        var indexedAt = DateTimeOffset.UtcNow;
        int processed = 0;

        foreach (string file in files)
        {
            progress?.Report(new SearchIndexProgress(processed, files.Count, Path.GetFileName(file)));
            processed++;

            byte[] content;
            try
            {
                content = session.ReadFile(file);
            }
            catch (VaultIntegrityException)
            {
                continue; // a corrupt document is Verify's business (Phase 3), not the index's
            }

            string? text = DocumentTextExtractor.Extract(file, new MemoryStream(content));
            database.Upsert(new IndexedDocument(
                RelativePath: file,
                LogicalName: Path.GetFileName(file),
                Category: CategoryFor(file),
                MimeType: MimeTypeFor(file),
                Size: content.Length,
                ModifiedAt: session.GetLastWriteTimeUtc(file),
                IndexedAt: indexedAt,
                Sha256: Sha256.ComputeHash(new MemoryStream(content)),
                Body: ComposeBody(file, text)));
        }

        database.RebuildFts();
        var index = new VaultSearchIndex(database, SearchIndexStatus.Built);
        index.Save(session);
        return index;
    }

    /// <summary>Persists the index rows into the vault as one encrypted file (spec section 9).</summary>
    public void Save(VaultSession session)
    {
        IReadOnlyList<IndexedDocument> documents = database.GetAll();
        using var target = new MemoryStream();
        JsonSerializer.Serialize(target, documents, JsonOptions);
        target.Position = 0;
        session.WriteFile(IndexPath, target);
    }

    /// <summary>Full-text search over names and document contents; empty query yields no results.</summary>
    public IReadOnlyList<SearchResultItem> Search(string query)
    {
        string sanitized = Fts5Query.Sanitize(query);
        return sanitized.Length == 0 ? [] : database.Search(sanitized);
    }

    /// <summary>Applies an update plan to the index: added/changed documents are re-extracted from the vault, deleted ones removed. The caller saves afterwards.</summary>
    public void ApplyChanges(VaultSession session, SyncPlan plan, ArchiveManifest manifest)
    {
        foreach ((ChangeKind kind, string relativePath) in plan.EnumerateChanges())
        {
            if (kind == ChangeKind.Deleted)
            {
                database.Remove(relativePath);
                continue;
            }

            byte[] content = session.ReadFile(relativePath);
            string? text = DocumentTextExtractor.Extract(relativePath, new MemoryStream(content));
            ManifestEntry? entry = manifest.Find(relativePath);
            database.Upsert(new IndexedDocument(
                RelativePath: relativePath,
                LogicalName: Path.GetFileName(relativePath),
                Category: CategoryFor(relativePath),
                MimeType: MimeTypeFor(relativePath),
                Size: content.Length,
                ModifiedAt: session.GetLastWriteTimeUtc(relativePath),
                IndexedAt: DateTimeOffset.UtcNow,
                Sha256: entry?.Sha256 ?? Sha256.ComputeHash(new MemoryStream(content)),
                Body: ComposeBody(relativePath, text)));
        }
    }

    /// <summary>Counts index entries whose SHA-256 no longer matches the manifest (spec section 16: index consistency).</summary>
    public int CountStaleEntries(ArchiveManifest manifest)
    {
        int stale = 0;
        foreach (IndexedDocument document in database.GetAll())
        {
            ManifestEntry? entry = manifest.Find(document.RelativePath);
            if (entry is null || !string.Equals(entry.Sha256, document.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                stale++;
            }
        }

        return stale;
    }

    /// <summary>
    /// The indexed body includes name and path so filename searches match (spec
    /// section 26), and CJK runs are segmented so every character is searchable.
    /// </summary>
    internal static string ComposeBody(string relativePath, string? extractedText)
    {
        string name = Path.GetFileName(relativePath);
        string body = string.IsNullOrEmpty(extractedText)
            ? $"{name}\n{relativePath}"
            : $"{name}\n{relativePath}\n{extractedText}";
        return CjkText.Segment(body);
    }

    /// <summary>Category defaults to the top-level folder name, or Other at the root (spec section 10).</summary>
    internal static string CategoryFor(string relativePath)
    {
        int separator = relativePath.IndexOf('/');
        return separator > 0 ? relativePath[..separator] : "Other";
    }

    internal static string MimeTypeFor(string relativePath)
    {
        return Path.GetExtension(relativePath.ToLowerInvariant()) switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".md" or ".markdown" => "text/markdown",
            ".html" or ".htm" => "text/html",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" or ".xlsm" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".csv" => "text/csv",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };
    }
}
