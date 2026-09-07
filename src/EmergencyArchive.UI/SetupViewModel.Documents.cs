using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>
/// Owner document management in Setup Mode: list the archived documents and
/// delete individual ones. Deleting removes the file from the vault, its entry
/// from the manifest, and its row from the search index — the same three places
/// an add touches — so browse and search stay consistent. This is destructive
/// (there is no recycle bin inside the archive), so the view confirms first.
/// </summary>
public sealed partial class SetupViewModel
{
    /// <summary>Every archived document (infrastructure files excluded), for the manage list.</summary>
    public ObservableCollection<string> ManageableDocuments { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteDocumentCommand))]
    private string? selectedDocumentPath;

    [ObservableProperty] private string manageDocumentsCountDisplay = "0 documents";

    /// <summary>Two-step delete confirmation: the button arms, then deletes.</summary>
    [ObservableProperty] private bool deleteArmed;

    public string DeleteButtonText => DeleteArmed ? "CLICK AGAIN TO CONFIRM DELETE" : "DELETE SELECTED";

    partial void OnDeleteArmedChanged(bool value) => OnPropertyChanged(nameof(DeleteButtonText));

    partial void OnSelectedDocumentPathChanged(string? value) => DeleteArmed = false;

    private bool CanDeleteDocument => !IsBusy && !string.IsNullOrEmpty(SelectedDocumentPath);

    public void ReloadManageableDocuments()
    {
        ManageableDocuments.Clear();
        foreach (string path in session.EnumerateFiles()
                     .Where(p => !VaultPaths.IsInfrastructurePath(p))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            ManageableDocuments.Add(path);
        }

        ManageDocumentsCountDisplay = ManageableDocuments.Count == 1
            ? "1 document"
            : $"{ManageableDocuments.Count:N0} documents";
    }

    /// <summary>Deletes the selected document from the vault, manifest, and index.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteDocument))]
    private async Task DeleteDocumentAsync()
    {
        string? path = SelectedDocumentPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // First click arms the confirmation; second click performs the delete.
        if (!DeleteArmed)
        {
            DeleteArmed = true;
            SetupStatus = $"Delete '{System.IO.Path.GetFileName(path)}'? Click DELETE again to confirm. This cannot be undone.";
            return;
        }

        DeleteArmed = false;
        IsBusy = true;
        SetupStatus = $"Deleting '{System.IO.Path.GetFileName(path)}'…";
        try
        {
            bool removed = await Task.Run(() => DeleteDocument(path));

            RefreshDashboard();
            OnDocumentsChanged();

            if (removed)
            {
                RecordActivity("Documents", $"Document deleted: {path}.");
                SetupStatus = $"Deleted '{System.IO.Path.GetFileName(path)}'.";
            }
            else
            {
                SetupStatus = $"'{System.IO.Path.GetFileName(path)}' was not found (already removed).";
            }

            SelectedDocumentPath = null;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or VaultException)
        {
            SetupStatus = $"Could not delete the document: {e.Message}";
            RecordActivity("Documents", $"Delete failed for {path}: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool DeleteDocument(string relativePath)
    {
        bool removed = session.RemoveFile(relativePath);
        if (!removed)
        {
            return false;
        }

        // Drop it from the manifest so the dashboard count and VERIFY stay right.
        ArchiveManifest? manifest = ManifestStore.Load(session);
        if (manifest is not null && manifest.Find(relativePath) is not null)
        {
            var entries = manifest.Entries
                .Where(e => !string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ManifestStore.Save(session, manifest with { Entries = entries });
        }

        // Remove it from the search index (build first if the index is absent).
        VaultSearchIndex? index = getIndex();
        if (index is null)
        {
            index = VaultSearchIndex.LoadOrBuild(session);
            setIndex(index);
        }

        var plan = new SyncPlan([], [], [relativePath]);
        index.ApplyChanges(session, plan, manifest ?? ArchiveManifest.Empty(session.VaultId, "not committed"));
        index.Save(session);

        return true;
    }
}
