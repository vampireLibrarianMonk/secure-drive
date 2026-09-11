using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>One editable extra field (key + value) in the credential editor.</summary>
public sealed partial class ExtraFieldEditorViewModel : ObservableObject
{
    public ExtraFieldEditorViewModel(string key = "", string value = "")
    {
        Key = key;
        Value = value;
    }

    [ObservableProperty]
    private string key;

    [ObservableProperty]
    private string value;
}

/// <summary>
/// The add/edit form for a single credential. Holds plain editable strings
/// (the password is shown in a normal box the user is actively typing into,
/// with a show/hide toggle) plus a dynamic list of extra key/value rows.
/// <see cref="Validate"/> enforces the minimum shape; <see cref="ToCredential"/>
/// produces the model to persist, preserving the id when editing.
/// </summary>
public sealed partial class CredentialEditorViewModel : ObservableObject
{
    /// <summary>Empty when adding; the existing id when editing.</summary>
    private readonly string? existingId;

    private CredentialEditorViewModel(string? existingId, string site, string username, string password)
    {
        this.existingId = existingId;
        this.site = site;
        this.username = username;
        this.password = password;
    }

    public static CredentialEditorViewModel ForNew() =>
        new(existingId: null, site: string.Empty, username: string.Empty, password: string.Empty);

    public static CredentialEditorViewModel ForEdit(Credential credential)
    {
        var editor = new CredentialEditorViewModel(
            credential.Id, credential.Site, credential.Username, credential.Password);
        foreach (CredentialField field in credential.EffectiveExtras)
        {
            editor.Extras.Add(new ExtraFieldEditorViewModel(field.Key, field.Value));
        }

        return editor;
    }

    public bool IsNew => existingId is null;

    public string Title => IsNew ? "Add credential" : "Edit credential";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string site;

    [ObservableProperty]
    private string username;

    [ObservableProperty]
    private string password;

    /// <summary>Whether the password box shows the value or masks it.</summary>
    [ObservableProperty]
    private bool isPasswordVisible;

    [ObservableProperty]
    private string? error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public ObservableCollection<ExtraFieldEditorViewModel> Extras { get; } = new();

    [RelayCommand]
    private void AddExtra() => Extras.Add(new ExtraFieldEditorViewModel());

    [RelayCommand]
    private void RemoveExtra(ExtraFieldEditorViewModel? field)
    {
        if (field is not null)
        {
            Extras.Remove(field);
        }
    }

    /// <summary>Requires a non-empty site (the only mandatory field).</summary>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Site))
        {
            Error = "A website or app name is required.";
            return false;
        }

        Error = null;
        return true;
    }

    /// <summary>
    /// Builds the model to persist. Reuses the existing id when editing so the
    /// store replaces in place; drops extra rows whose key is blank.
    /// </summary>
    public Credential ToCredential()
    {
        List<CredentialField> extras = Extras
            .Where(e => !string.IsNullOrWhiteSpace(e.Key))
            .Select(e => new CredentialField(e.Key.Trim(), e.Value ?? string.Empty))
            .ToList();

        string id = existingId ?? Guid.NewGuid().ToString("N");
        return new Credential(
            id,
            Site.Trim(),
            Username?.Trim() ?? string.Empty,
            Password ?? string.Empty,
            extras);
    }
}
