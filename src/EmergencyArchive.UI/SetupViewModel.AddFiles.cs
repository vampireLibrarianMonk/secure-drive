using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>
/// Adds individual documents to the archive directly (spec section 13
/// complement to folder sources): the owner picks one or more files, chooses a
/// category folder, and the files are written into the vault, added to the
/// manifest, and indexed — without configuring a source folder or running a
/// full UPDATE. Directly-added files live under the chosen category (e.g.
/// <c>Legal/will.pdf</c>) and survive later source-based updates (an UPDATE only
/// deletes files tracked from a removed source scan, never these).
/// </summary>
public sealed partial class SetupViewModel
{
    /// <summary>Category folders offered when adding a single document.</summary>
    public IReadOnlyList<string> ImportCategories { get; } = DocumentCategories.Default;

    [ObservableProperty] private string selectedImportCategory = "Other";

    /// <summary>
    /// Called by the view after the file picker returns one or more local file
    /// paths. Writes each into the vault under <see cref="SelectedImportCategory"/>,
    /// records it in the manifest, and indexes it for search.
    /// </summary>
    public async void AddFilesFromPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        string category = string.IsNullOrWhiteSpace(SelectedImportCategory) ? "Other" : SelectedImportCategory.Trim();

        IsBusy = true;
        SetupStatus = paths.Count == 1 ? "Adding document…" : $"Adding {paths.Count} documents…";
        try
        {
            (int added, int skipped) = await Task.Run(() => ImportFiles(paths, category));

            RefreshDashboard();
            OnDocumentsChanged();

            string summary = added == 1
                ? $"Added 1 document to '{category}'."
                : $"Added {added} documents to '{category}'.";
            if (skipped > 0)
            {
                summary += $" Skipped {skipped} unreadable file(s).";
            }

            RecordActivity("Documents", summary);
            SetupStatus = added > 0
                ? summary + " It is searchable now."
                : "No documents were added.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or VaultException)
        {
            SetupStatus = $"Could not add the document(s): {e.Message}";
            RecordActivity("Documents", $"Add document failed: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Writes the picked files into the vault, updates the manifest and search index. Returns (added, skipped).</summary>
    private (int Added, int Skipped) ImportFiles(IReadOnlyList<string> paths, string category)
    {
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty(session.VaultId, "not committed");

        var entries = new List<ManifestEntry>(manifest.Entries);
        var addedRelativePaths = new List<string>();
        int skipped = 0;

        foreach (string sourcePath in paths)
        {
            if (!File.Exists(sourcePath))
            {
                skipped++;
                continue;
            }

            byte[] content;
            try
            {
                content = File.ReadAllBytes(sourcePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                skipped++;
                continue;
            }

            string relativePath = UniqueVaultPath(category, Path.GetFileName(sourcePath), entries, addedRelativePaths);
            session.WriteFile(relativePath, content);

            var modified = File.GetLastWriteTimeUtc(sourcePath);
            entries.RemoveAll(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            entries.Add(new ManifestEntry(relativePath, content.Length, modified, Sha256.ComputeHash(new MemoryStream(content)))
            {
                Source = ManifestSource.Manual,
            });
            addedRelativePaths.Add(relativePath);
        }

        if (addedRelativePaths.Count == 0)
        {
            return (0, skipped);
        }

        // Persist the manifest so the dashboard count and VERIFY stay accurate.
        ArchiveManifest updated = manifest with { Entries = entries };
        ManifestStore.Save(session, updated);

        // Index the new documents incrementally (build the index if absent).
        VaultSearchIndex? index = getIndex();
        if (index is null)
        {
            index = VaultSearchIndex.LoadOrBuild(session);
            setIndex(index);
        }

        var plan = new SyncPlan(addedRelativePaths, [], []);
        index.ApplyChanges(session, plan, updated);
        index.Save(session);

        return (addedRelativePaths.Count, skipped);
    }

    /// <summary>Builds a vault path under the category, avoiding collisions with existing or just-added files.</summary>
    private static string UniqueVaultPath(string category, string fileName, IReadOnlyList<ManifestEntry> existing, IReadOnlyList<string> pending)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);

        string Candidate(int n) => n == 0 ? $"{category}/{fileName}" : $"{category}/{name} ({n}){ext}";

        bool Taken(string path) =>
            existing.Any(e => string.Equals(e.RelativePath, path, StringComparison.OrdinalIgnoreCase))
            || pending.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

        int suffix = 0;
        while (Taken(Candidate(suffix)))
        {
            suffix++;
        }

        return Candidate(suffix);
    }
}
