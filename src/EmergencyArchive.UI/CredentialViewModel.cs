using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>
/// The in-app password manager viewer (native Avalonia — no browser/WebView2,
/// so it inherits the app's platform support and the "nothing installed"
/// guarantee). Loads credentials from the encrypted vault via
/// <see cref="CredentialStore"/> and presents a searchable list; every secret
/// value is obfuscated until revealed. Nothing is written to disk.
/// </summary>
public sealed partial class CredentialViewModel : ObservableObject
{
    /// <summary>All loaded credentials (source of the filter).</summary>
    private readonly List<CredentialItemViewModel> all = new();

    public CredentialViewModel(VaultSession session)
    {
        CredentialDatabase database = CredentialStore.Load(session);
        foreach (Credential credential in database.Entries
                     .OrderBy(c => c.Site, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => c.Username, StringComparer.OrdinalIgnoreCase))
        {
            all.Add(new CredentialItemViewModel(credential));
        }

        ApplyFilter();
    }

    /// <summary>The rows currently shown (after the search filter).</summary>
    public ObservableCollection<CredentialItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private string summary = string.Empty;

    public bool IsEmpty => all.Count == 0;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

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
            ? "No credentials yet. Import a KeePass file or add entries."
            : string.IsNullOrWhiteSpace(term)
                ? $"{all.Count} credential(s)"
                : $"Showing {shown} of {all.Count} credential(s)";
    }
}
