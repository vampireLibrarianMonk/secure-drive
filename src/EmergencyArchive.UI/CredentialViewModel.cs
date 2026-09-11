using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

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
            ? "No credentials yet. Add one, or import a KeePass file (coming soon)."
            : string.IsNullOrWhiteSpace(term)
                ? $"{all.Count} credential(s)"
                : $"Showing {shown} of {all.Count} credential(s)";
    }
}
