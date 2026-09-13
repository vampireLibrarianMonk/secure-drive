using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Kdbx;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>
/// The in-app password manager viewer (native Avalonia — no browser/WebView2,
/// so it inherits the app's platform support and the "nothing installed"
/// guarantee). Loads credentials from the encrypted vault via
/// <see cref="CredentialStore"/> and presents a searchable list with full CRUD;
/// every secret value is obfuscated until revealed. All changes are written
/// back through the vault (encrypted); nothing is written to disk in the clear.
/// </summary>
public sealed partial class CredentialViewModel : ObservableObject
{
    /// <summary>The unlocked vault this viewer reads from and writes to.</summary>
    private readonly VaultSession session;

    /// <summary>Optional sink for activity-log lines (label only, never secrets).</summary>
    private readonly Action<string>? log;

    /// <summary>The authoritative model, persisted on every change.</summary>
    private CredentialDatabase database;

    /// <summary>All loaded rows (source of the search filter).</summary>
    private readonly List<CredentialItemViewModel> all = new();

    public CredentialViewModel(VaultSession session, Action<string>? log = null)
    {
        this.session = session;
        this.log = log;
        database = CredentialStore.Load(session);
        Rebuild();
    }

    /// <summary>The rows currently shown (after the search filter).</summary>
    public ObservableCollection<CredentialItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private string summary = string.Empty;

    /// <summary>The add/edit form, non-null only while editing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private CredentialEditorViewModel? editor;

    /// <summary>The currently selected row (kept for future keyboard/selection UX).</summary>
    [ObservableProperty]
    private CredentialItemViewModel? selectedItem;

    public bool IsEmpty => all.Count == 0;

    public bool IsEditing => Editor is not null;

    /// <summary>True while the "clear all credentials" confirmation is showing.</summary>
    [ObservableProperty]
    private bool isConfirmingClearAll;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    // --- Clear all (purge the password store, leaves documents untouched) ----

    /// <summary>Asks for confirmation before wiping every stored credential.</summary>
    [RelayCommand]
    private void ClearAll() => IsConfirmingClearAll = true;

    /// <summary>Dismisses the clear-all confirmation without deleting anything.</summary>
    [RelayCommand]
    private void CancelClearAll() => IsConfirmingClearAll = false;

    /// <summary>
    /// Permanently removes every stored credential (saves an empty database).
    /// Only the credentials are affected; archived documents are untouched.
    /// </summary>
    [RelayCommand]
    private void ConfirmClearAll()
    {
        int removed = database.Count;
        database = CredentialDatabase.Empty;
        Persist();
        IsConfirmingClearAll = false;
        Rebuild();
        log?.Invoke($"Cleared all {removed} stored credential(s).");
    }

    // --- KeePass (KDBX) import / export -------------------------------------

    /// <summary>Which KeePass password prompt (if any) is currently showing.</summary>
    public enum KdbxMode
    {
        None,
        Import,
        Export,
    }

    /// <summary>The bytes of a chosen .kdbx file, awaiting the password (import).</summary>
    private byte[]? pendingImportBytes;

    /// <summary>
    /// Callback the view sets for export: given the produced .kdbx bytes, the
    /// view writes them to the file the user chose. Keeps file I/O in the view.
    /// </summary>
    private Func<byte[], Task>? exportWriter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKdbxPrompt))]
    [NotifyPropertyChangedFor(nameof(KdbxPromptTitle))]
    private KdbxMode kdbxPromptMode;

    [ObservableProperty]
    private string kdbxPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKdbxError))]
    private string? kdbxError;

    public bool IsKdbxPrompt => KdbxPromptMode != KdbxMode.None;

    public bool HasKdbxError => !string.IsNullOrEmpty(KdbxError);

    public string KdbxPromptTitle => KdbxPromptMode switch
    {
        KdbxMode.Import => "Import from KeePass (.kdbx)",
        KdbxMode.Export => "Export to KeePass (.kdbx)",
        _ => string.Empty,
    };

    /// <summary>The view calls this after the user picks a .kdbx file to import.</summary>
    public void BeginImport(byte[] fileBytes)
    {
        pendingImportBytes = fileBytes;
        exportWriter = null;
        KdbxPassword = string.Empty;
        KdbxError = null;
        KdbxPromptMode = KdbxMode.Import;
    }

    /// <summary>The view calls this after the user picks a save location to export to.</summary>
    public void BeginExport(Func<byte[], Task> writer)
    {
        exportWriter = writer;
        pendingImportBytes = null;
        KdbxPassword = string.Empty;
        KdbxError = null;
        KdbxPromptMode = KdbxMode.Export;
    }

    [RelayCommand]
    private void CancelKdbx()
    {
        KdbxPromptMode = KdbxMode.None;
        KdbxPassword = string.Empty;
        KdbxError = null;
        pendingImportBytes = null;
        exportWriter = null;
    }

    /// <summary>Confirms the KDBX prompt: runs the import or export with the entered password.</summary>
    [RelayCommand]
    private async Task ConfirmKdbxAsync()
    {
        if (string.IsNullOrEmpty(KdbxPassword))
        {
            KdbxError = "Enter the KeePass file's password.";
            return;
        }

        try
        {
            if (KdbxPromptMode == KdbxMode.Import)
            {
                ImportFromKdbx(pendingImportBytes ?? [], KdbxPassword);
            }
            else if (KdbxPromptMode == KdbxMode.Export && exportWriter is not null)
            {
                byte[] bytes = KdbxCredentialMapper.Export(database, KdbxPassword);
                await exportWriter(bytes);
                log?.Invoke($"Exported {database.Count} credential(s) to a KeePass file.");
            }

            CancelKdbx();
        }
        catch (KdbxAuthenticationException)
        {
            KdbxError = "Wrong password for the KeePass file.";
        }
        catch (KdbxException e)
        {
            AppLog.Handled("ConfirmKdbx", e);
            KdbxError = "That file is not a supported KeePass database.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Handled("ConfirmKdbx (file io)", e);
            KdbxError = $"Could not complete: {e.Message}";
        }
    }

    /// <summary>
    /// Merges the entries from a KeePass file into the store (each gets a fresh
    /// id) and persists. Only counts are logged — never secret values.
    /// </summary>
    private void ImportFromKdbx(byte[] fileBytes, string kdbxPassword)
    {
        IReadOnlyList<Credential> imported = KdbxCredentialMapper.Import(fileBytes, kdbxPassword);
        foreach (Credential credential in imported)
        {
            database = database.With(credential);
        }

        Persist();
        Rebuild();
        log?.Invoke($"Imported {imported.Count} credential(s) from a KeePass file.");
    }

    // --- Commands -----------------------------------------------------------

    /// <summary>Opens a blank form to add a new credential.</summary>
    [RelayCommand]
    private void Add() => Editor = CredentialEditorViewModel.ForNew();

    /// <summary>Opens the form pre-filled with the given credential row.</summary>
    [RelayCommand]
    private void Edit(CredentialItemViewModel? item)
    {
        CredentialItemViewModel? target = item ?? SelectedItem;
        if (target is null)
        {
            return;
        }

        SelectedItem = target;
        Credential? current = database.Find(target.Id);
        if (current is null)
        {
            return;
        }

        Editor = CredentialEditorViewModel.ForEdit(current);
    }

    /// <summary>Discards the form without saving.</summary>
    [RelayCommand]
    private void CancelEdit() => Editor = null;

    /// <summary>Validates and persists the form, then refreshes the list.</summary>
    [RelayCommand]
    private void Save()
    {
        if (Editor is null || !Editor.Validate())
        {
            return;
        }

        Credential credential = Editor.ToCredential();
        bool isNew = database.Find(credential.Id) is null;

        database = database.With(credential);
        Persist();

        log?.Invoke(isNew
            ? $"Added credential for '{credential.Site}'."
            : $"Updated credential for '{credential.Site}'.");

        Editor = null;
        Rebuild();
        SelectById(credential.Id);
    }

    /// <summary>Deletes the given credential row (or the selection) and refreshes the list.</summary>
    [RelayCommand]
    private void Delete(CredentialItemViewModel? item)
    {
        CredentialItemViewModel? target = item ?? SelectedItem;
        if (target is null)
        {
            return;
        }

        string site = target.Site;
        database = database.Without(target.Id);
        Persist();
        log?.Invoke($"Deleted credential for '{site}'.");

        SelectedItem = null;
        Editor = null;
        Rebuild();
    }

    // --- Internals ----------------------------------------------------------

    private void Persist() => CredentialStore.Save(session, database);

    private void SelectById(string id) =>
        SelectedItem = Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    /// <summary>Rebuilds the row list from the current database and re-applies the filter.</summary>
    private void Rebuild()
    {
        all.Clear();
        foreach (Credential credential in database.Entries
                     .OrderBy(c => c.Site, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => c.Username, StringComparer.OrdinalIgnoreCase))
        {
            all.Add(new CredentialItemViewModel(credential));
        }

        ApplyFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ApplyFilter()
    {
        string? term = SearchText?.Trim();

        Items.Clear();
        int shown = 0;
        foreach (CredentialItemViewModel item in all)
        {
            if (item.Matches(term))
            {
                Items.Add(item);
                shown++;
            }
        }

        Summary = all.Count == 0
            ? "No credentials yet. Add one, or import a KeePass (.kdbx) file."
            : string.IsNullOrWhiteSpace(term)
                ? $"{all.Count} credential(s)"
                : $"Showing {shown} of {all.Count} credential(s)";
    }
}
