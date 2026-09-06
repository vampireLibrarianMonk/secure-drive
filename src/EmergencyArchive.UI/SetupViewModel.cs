using System.Collections.ObjectModel;
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
/// Setup Mode (spec section 12): archive health dashboard, source management,
/// update, verify, index rebuild, password change, and recovery-instructions
/// export. Reached only after normal authentication; the vault session stays
/// owned by <see cref="MainWindowViewModel"/>.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly VaultSession session;
    private readonly string vaultPath;
    private readonly Func<VaultSearchIndex?> getIndex;
    private readonly Action<VaultSearchIndex?> setIndex;

    public const string RecoveryInstructionsFileName = "RECOVERY-INSTRUCTIONS.txt";

    public SetupViewModel(VaultSession session, string vaultPath, Func<VaultSearchIndex?> getIndex, Action<VaultSearchIndex?> setIndex)
    {
        this.session = session;
        this.vaultPath = vaultPath;
        this.getIndex = getIndex;
        this.setIndex = setIndex;
        RefreshDashboard();
        ReloadSources();
    }

    // --- Dashboard ----------------------------------------------------------

    [ObservableProperty] private string? archiveId;

    [ObservableProperty] private string archiveVersion = "not committed";

    [ObservableProperty] private string documentCountDisplay = "0";

    [ObservableProperty] private string archiveSizeDisplay = "0 B";

    [ObservableProperty] private string driveFreeDisplay = "—";

    [ObservableProperty] private string lastUpdateDisplay = "Never";

    [ObservableProperty] private string integrityStatus = "Not verified yet — run VERIFY ARCHIVE.";

    [ObservableProperty] private int sourceCount;

    // --- Shared state -------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private bool isBusy;

    [ObservableProperty] private string? setupStatus = string.Empty;

    public ObservableCollection<SourceItemViewModel> Sources { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    private SourceItemViewModel? selectedSource;

    // --- Change password fields ---------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private string? newPassword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private string? confirmNewPassword;

    private bool CanChangePassword => !IsBusy
        && !string.IsNullOrEmpty(NewPassword)
        && !string.IsNullOrEmpty(ConfirmNewPassword);

    private bool CanUpdateOrVerify => !IsBusy;

    private bool CanRebuildIndex => !IsBusy;

    /// <summary>Raised when an update changed the stored documents; the browse screen reloads.</summary>
    public event EventHandler? DocumentsChanged;
}

/// <summary>One configured source directory shown in Setup Mode.</summary>
public sealed record SourceItemViewModel(string Alias, string Path);
