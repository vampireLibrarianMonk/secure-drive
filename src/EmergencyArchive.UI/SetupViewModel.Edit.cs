using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>
/// The "update" half of document CRUD (rename, move/re-categorize, replace
/// content). The vault has no native rename/move, so each operation is composed
/// from write-new + remove-old and then reconciled with the manifest and the
/// search index — the same three places add and delete touch — so browse and
/// search stay consistent. A document's provenance (<see cref="ManifestSource"/>)
/// is preserved across rename/move so folder updates keep treating it correctly.
/// </summary>
public sealed partial class SetupViewModel
{
    // --- Edit fields bound to the Manage Documents card ---------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameDocumentCommand))]
    private string? renameText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDocumentCommand))]
    private string? moveTargetCategory;

    /// <summary>When a document is selected, prefill the rename box with its name and the move box with its category.</summary>
    private void SyncEditFieldsToSelection()
    {
        if (string.IsNullOrEmpty(SelectedDocumentPath))
        {
            RenameText = null;
            MoveTargetCategory = null;
            return;
        }

        RenameText = Path.GetFileName(SelectedDocumentPath);
        string current = CategoryOf(SelectedDocumentPath);
        MoveTargetCategory = ImportCategories.Contains(current) ? current : "Other";
    }

    private static string CategoryOf(string relativePath)
    {
        int slash = relativePath.IndexOf('/');
        return slash > 0 ? relativePath[..slash] : "Other";
    }

    private bool CanRenameDocument =>
        !IsBusy
        && !string.IsNullOrEmpty(SelectedDocumentPath)
        && !string.IsNullOrWhiteSpace(RenameText)
        && RenameText.Trim() != Path.GetFileName(SelectedDocumentPath)
        && RenameText.IndexOfAny(['/', '\\']) < 0;

    private bool CanMoveDocument =>
        !IsBusy
        && !string.IsNullOrEmpty(SelectedDocumentPath)
        && !string.IsNullOrWhiteSpace(MoveTargetCategory)
        && !string.Equals(MoveTargetCategory, CategoryOf(SelectedDocumentPath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Public so the REPLACE FILE button (a code-behind file-picker handler) can bind IsEnabled.</summary>
    public bool CanReplaceDocument => !IsBusy && !string.IsNullOrEmpty(SelectedDocumentPath);

    // --- Rename -------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRenameDocument))]
    private async Task RenameDocumentAsync()
    {
        string? source = SelectedDocumentPath;
        string? newName = RenameText?.Trim();
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(newName))
        {
            return;
        }

        string category = CategoryOf(source);
        string target = $"{category}/{newName}";
        await RelocateAsync(source, target, "renamed", $"Renamed to '{newName}'.");
    }

    // --- Move / re-categorize ----------------------------------------------

    [RelayCommand(CanExecute = nameof(CanMoveDocument))]
    private async Task MoveDocumentAsync()
    {
        string? source = SelectedDocumentPath;
        string? category = MoveTargetCategory?.Trim();
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(category))
        {
            return;
        }

        string target = $"{category}/{Path.GetFileName(source)}";
        await RelocateAsync(source, target, "moved", $"Moved to '{category}'.");
    }

    // --- Replace content ----------------------------------------------------

    /// <summary>Called by the view after the file picker returns a replacement file path.</summary>
    public async void ReplaceDocumentFromPath(string replacementLocalPath)
    {
        string? target = SelectedDocumentPath;
        if (string.IsNullOrEmpty(target) || !File.Exists(replacementLocalPath))
        {
            return;
        }

        IsBusy = true;
        SetupStatus = $"Replacing contents of '{Path.GetFileName(target)}'…";
        try
        {
            byte[] content = File.ReadAllBytes(replacementLocalPath);
            var modified = File.GetLastWriteTimeUtc(replacementLocalPath);
            await Task.Run(() => WriteAndReconcile(target, content, modified, keepSourceOf: target, removePath: null));

            RefreshDashboard();
            OnDocumentsChanged();
            SelectedDocumentPath = target;
            RecordActivity("Documents", $"Replaced contents: {target}.");
            SetupStatus = $"Replaced the contents of '{Path.GetFileName(target)}'. It is re-indexed now.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or VaultException)
        {
            AppLog.Handled("ReplaceDocument", e);
            SetupStatus = $"Could not replace the document: {e.Message}";
            RecordActivity("Documents", $"Replace failed for {target}: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Shared relocate (rename + move) ------------------------------------

    private async Task RelocateAsync(string source, string desiredTarget, string verb, string successDetail)
    {
        IsBusy = true;
        SetupStatus = $"Working on '{Path.GetFileName(source)}'…";
        try
        {
            string finalTarget = await Task.Run(() =>
            {
                // Read current content, choose a non-colliding target, write it
                // there, then remove the old path. Provenance is preserved.
                byte[] content = session.ReadFile(source);
                string target = UniqueVaultPathForExisting(desiredTarget);
                DateTimeOffset modified = SafeLastWrite(source);
                WriteAndReconcile(target, content, modified, keepSourceOf: source, removePath: source);
                return target;
            });

            RefreshDashboard();
            OnDocumentsChanged();
            SelectedDocumentPath = finalTarget;
            RecordActivity("Documents", $"Document {verb}: {source} -> {finalTarget}.");
            SetupStatus = successDetail;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or VaultException)
        {
            AppLog.Handled($"RelocateDocument ({verb})", e);
            SetupStatus = $"Could not {verb.TrimEnd('d')} the document: {e.Message}";
            RecordActivity("Documents", $"{verb} failed for {source}: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="target"/>, optionally
    /// removes <paramref name="removePath"/> (for rename/move), and updates the
    /// manifest (preserving the provenance of <paramref name="keepSourceOf"/>)
    /// and the search index so all three stay consistent.
    /// </summary>
    private void WriteAndReconcile(string target, byte[] content, DateTimeOffset modified, string keepSourceOf, string? removePath)
    {
        session.WriteFile(target, content);
        if (removePath is not null && !string.Equals(removePath, target, StringComparison.OrdinalIgnoreCase))
        {
            session.RemoveFile(removePath);
        }

        // Manifest: preserve the moved/renamed document's provenance.
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty(session.VaultId, "not committed");
        ManifestSource source = manifest.Find(keepSourceOf)?.Source ?? ManifestSource.Manual;

        var entries = manifest.Entries
            .Where(e => !string.Equals(e.RelativePath, target, StringComparison.OrdinalIgnoreCase)
                     && !(removePath is not null && string.Equals(e.RelativePath, removePath, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        entries.Add(new ManifestEntry(target, content.Length, modified, Sha256.ComputeHash(new MemoryStream(content)))
        {
            Source = source,
        });
        ArchiveManifest updated = manifest with { Entries = entries };
        ManifestStore.Save(session, updated);

        // Search index: drop the old path (if any) and (re)add the target.
        VaultSearchIndex? index = getIndex();
        if (index is null)
        {
            index = VaultSearchIndex.LoadOrBuild(session);
            setIndex(index);
        }

        List<string> deleted = removePath is not null && !string.Equals(removePath, target, StringComparison.OrdinalIgnoreCase)
            ? [removePath]
            : [];
        index.ApplyChanges(session, new SyncPlan([target], [], deleted), updated);
        index.Save(session);
    }

    private DateTimeOffset SafeLastWrite(string relativePath)
    {
        try
        {
            return session.GetLastWriteTimeUtc(relativePath);
        }
        catch (Exception e) when (e is IOException or VaultException)
        {
            AppLog.Handled("SafeLastWrite (falling back to current time)", e);
            return DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Like the add-files collision guard, but for an existing document set (manifest + vault).</summary>
    private string UniqueVaultPathForExisting(string desiredPath)
    {
        if (!session.FileExists(desiredPath))
        {
            return desiredPath;
        }

        string dir = desiredPath.Contains('/') ? desiredPath[..desiredPath.LastIndexOf('/')] : string.Empty;
        string fileName = Path.GetFileName(desiredPath);
        string name = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);

        int n = 1;
        while (true)
        {
            string candidate = string.IsNullOrEmpty(dir) ? $"{name} ({n}){ext}" : $"{dir}/{name} ({n}){ext}";
            if (!session.FileExists(candidate))
            {
                return candidate;
            }

            n++;
        }
    }
}
