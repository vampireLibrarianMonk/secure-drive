using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmergencyArchive.UI;

/// <summary>
/// View model for the emergency startup screen (spec section 7): one password
/// field and one unlock button.
/// </summary>
/// <remarks>
/// Phase 0 scaffold: the password is accepted and immediately discarded; the
/// vault handshake arrives with Phase 1 (Minimum Viable Vault). From Phase 1 on,
/// password material must avoid immutable managed strings where practical and be
/// cleared after use (spec section 19); the immediate discard here follows that
/// direction already.
/// </remarks>
public partial class MainWindowViewModel : ObservableObject
{
    public const string Phase1Notice =
        "Vault unlock is implemented in Phase 1 (Minimum Viable Vault). " +
        "No data is stored or read yet.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string? _password;

    [ObservableProperty]
    private string? _statusMessage = "Enter the archive password to continue.";

    private bool CanUnlock() => !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private void Unlock()
    {
        // Discard immediately — never persist or log password material.
        Password = null;

        StatusMessage = Phase1Notice;
    }
}
