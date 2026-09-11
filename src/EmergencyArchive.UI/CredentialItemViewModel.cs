using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>
/// A single secret value shown in the credential viewer: obfuscated by default,
/// with a reveal toggle. The underlying value is only exposed through
/// <see cref="RevealedValue"/> / <see cref="CopyValue"/>; the bound display
/// (<see cref="Display"/>) shows dots until revealed.
/// </summary>
public sealed partial class SecretFieldViewModel : ObservableObject
{
    private const string Mask = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";

    public SecretFieldViewModel(string label, string value)
    {
        Label = label;
        RevealedValue = value;
    }

    public string Label { get; }

    /// <summary>The real value — used for copy and shown only when revealed.</summary>
    public string RevealedValue { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    [NotifyPropertyChangedFor(nameof(RevealButtonText))]
    private bool isRevealed;

    /// <summary>What the UI binds to: dots when hidden, the value when revealed.</summary>
    public string Display => IsRevealed ? RevealedValue : Mask;

    public string RevealButtonText => IsRevealed ? "Hide" : "Reveal";

    /// <summary>The value to place on the clipboard (always the real value).</summary>
    public string CopyValue => RevealedValue;
}

/// <summary>One credential row in the viewer: site, username, password, and expandable extras.</summary>
public sealed partial class CredentialItemViewModel : ObservableObject
{
    public CredentialItemViewModel(Credential credential)
    {
        Id = credential.Id;
        Site = string.IsNullOrEmpty(credential.Site) ? "(no site)" : credential.Site;
        Username = credential.Username ?? string.Empty;
        Password = new SecretFieldViewModel("Password", credential.Password ?? string.Empty);

        foreach (CredentialField field in credential.EffectiveExtras)
        {
            Extras.Add(new SecretFieldViewModel(field.Key, field.Value));
        }
    }

    /// <summary>Stable identifier used to find/update/delete this credential in the store.</summary>
    public string Id { get; }

    public string Site { get; }

    public string Username { get; }

    public SecretFieldViewModel Password { get; }

    public ObservableCollection<SecretFieldViewModel> Extras { get; } = new();

    public bool HasExtras => Extras.Count > 0;

    public int ExtrasCount => Extras.Count;

    [ObservableProperty]
    private bool isExtrasExpanded;

    /// <summary>Case-insensitive match on site or username (mirrors the document search filter).</summary>
    public bool Matches(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        return Site.Contains(term, StringComparison.OrdinalIgnoreCase)
            || Username.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
