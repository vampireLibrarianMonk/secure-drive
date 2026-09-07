using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>
/// First-run "create a new archive" flow (owner setup without the CLI). When
/// the application starts and no vault is found on the drive, the create screen
/// is shown instead of the password screen: the owner picks a password, the
/// vault is created at the drive-root <c>vault\</c> (spec section 4), and the
/// app transitions straight into the unlocked archive so the owner can go to
/// SETUP → ESTATE PLANNING immediately.
/// </summary>
public partial class MainWindowViewModel
{
    public const string FirstRunMessage =
        "No archive found on this drive yet.\nCreate one below to get started.";

    public const string CreatingMessage =
        "Creating the encrypted archive… (deriving the key takes a moment)";

    /// <summary>True when the drive has no vault: show the create screen, not the password screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreating))]
    [NotifyPropertyChangedFor(nameof(IsUnlockable))]
    private bool isFirstRun;

    /// <summary>The create screen is visible on first run while still locked.</summary>
    public bool IsCreating => IsFirstRun && !IsUnlocked;

    /// <summary>The password (unlock) screen is visible when a vault exists and we are locked.</summary>
    public bool IsUnlockable => !IsFirstRun && !IsUnlocked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateArchiveCommand))]
    private string? newArchivePassword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateArchiveCommand))]
    private string? confirmArchivePassword;

    private bool CanCreateArchive => !IsBusy
        && !string.IsNullOrEmpty(NewArchivePassword)
        && !string.IsNullOrEmpty(ConfirmArchivePassword);

    /// <summary>
    /// Detects whether a vault exists and sets the initial screen. Called from
    /// the constructor; safe to call again (e.g. after creation).
    /// </summary>
    public void DetectVault()
    {
        bool vaultExists = VaultLocator.Locate() is not null;
        IsFirstRun = !vaultExists;
        StatusMessage = vaultExists ? ReadyMessage : FirstRunMessage;
    }

    [RelayCommand(CanExecute = nameof(CanCreateArchive))]
    private async Task CreateArchiveAsync()
    {
        string password = NewArchivePassword ?? string.Empty;
        string confirm = ConfirmArchivePassword ?? string.Empty;

        if (password != confirm)
        {
            StatusMessage = "The two passwords do not match. Please re-enter them.";
            return;
        }

        IReadOnlyList<string> violations = PasswordPolicy.Validate(password);
        if (violations.Count > 0)
        {
            StatusMessage = string.Join("\n", violations);
            return;
        }

        string target = VaultLocator.LocateCreatable();

        IsBusy = true;
        StatusMessage = CreatingMessage;
        try
        {
            Directory.CreateDirectory(target);
            await Task.Run(() => VaultStore.Create(target, password));

            // Clear the create-screen copies of the password as early as possible.
            NewArchivePassword = null;
            ConfirmArchivePassword = null;

            // The vault now exists; unlock it directly into the archive screen.
            IsFirstRun = false;
            OnPropertyChanged(nameof(IsCreating));
            OnPropertyChanged(nameof(IsUnlockable));

            VaultSession opened = await Task.Run(() => VaultStore.Unlock(target, password));
            session = opened;
            vaultPath = target;
            operationLog = OperationLogStore.Load(session);
            operationLog.Append("Vault", "Archive created.");
            OperationLogStore.Save(session, operationLog);
            LoadDocuments();
            IsUnlocked = true;
            StatusMessage =
                "Archive created and unlocked. Click SETUP, then RUN ESTATE-PLANNING SETUP to add " +
                "your folders and a letter for your family, then add your documents and UPDATE.";
            _ = IndexInBackgroundAsync();
        }
        catch (Exception e) when (e is VaultException or IOException or UnauthorizedAccessException)
        {
            IsFirstRun = true;
            OnPropertyChanged(nameof(IsCreating));
            OnPropertyChanged(nameof(IsUnlockable));
            StatusMessage = $"Could not create the archive: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
