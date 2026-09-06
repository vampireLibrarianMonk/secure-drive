using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;

namespace EmergencyArchive.UI;

/// <summary>
/// Orchestrates the two application screens: the emergency password screen
/// (spec section 7) and the read-only archive screen, including the 5 second
/// rate limit between password attempts (spec section 19).
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly AttemptRateLimiter rateLimiter = new();
    private VaultSession? session;
    private string? openTempDirectory;

    public const string ReadyMessage = "Enter the archive password to continue.";
    public const string UnlockingMessage = "Unlocking archive… (deriving the key takes a moment)";
    public const string WrongPasswordMessage = "Unable to unlock archive.\nCheck the password and try again.";
    public const string IntegrityMessage = "Archive integrity problem detected.\nDo not modify this USB.\nTry another archive replica or follow RECOVERY-INSTRUCTIONS.txt.";
    public const string UnlockedMessage = "Archive unlocked — READ ONLY. Select a document, then OPEN or EXPORT / COPY.";
    public const string NoVaultMessage = "No archive found on this drive.\nContact the archive owner or see RECOVERY-INSTRUCTIONS.txt.";

    // --- Password screen state ----------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string? password;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool isCooldownActive;

    [ObservableProperty] private string? statusMessage;

    // --- Archive screen state -----------------------------------------------

    [ObservableProperty] private bool isUnlocked;

    [ObservableProperty] private string? searchText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDocument))]
    private DocumentItemViewModel? selectedDocument;

    public ObservableCollection<DocumentItemViewModel> Documents { get; } = new();

    public ObservableCollection<DocumentItemViewModel> FilteredDocuments { get; } = new();

    public bool HasSelectedDocument => SelectedDocument is not null;

    private bool CanAttemptUnlock => !IsBusy && !IsCooldownActive && !string.IsNullOrEmpty(Password);

    // --- Commands -----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAttemptUnlock))]
    private async Task UnlockAsync()
    {
        string? vaultPath = VaultLocator.Locate();
        if (vaultPath is null)
        {
            StatusMessage = NoVaultMessage;
            return;
        }

        IsBusy = true;
        StatusMessage = UnlockingMessage;

        // Register the attempt BEFORE the work: the 5 second window (spec §19)
        // spans one attempt to the next, regardless of the outcome.
        rateLimiter.RegisterAttempt(DateTimeOffset.UtcNow);
        string password = Password ?? string.Empty;
        Password = null; // discard the managed copy as early as possible

        try
        {
            VaultSession opened = await Task.Run(() => VaultStore.Unlock(vaultPath, password));
            session = opened;
            LoadDocuments();
            IsUnlocked = true;
            StatusMessage = UnlockedMessage;
        }
        catch (VaultUnlockException)
        {
            StatusMessage = WrongPasswordMessage;
            await RunCooldownAsync(rateLimiter.RemainingDelay(DateTimeOffset.UtcNow));
        }
        catch (VaultException)
        {
            // Corrupt, tampered, or unsupported vault: never reveal details.
            StatusMessage = IntegrityMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Locks the archive (also invoked when the window closes, spec section 21).</summary>
    [RelayCommand]
    private void Lock()
    {
        CleanupTempExports();
        session?.Dispose();
        session = null;
        Documents.Clear();
        FilteredDocuments.Clear();
        SelectedDocument = null;
        SearchText = null;
        IsUnlocked = false;
        StatusMessage = "Archive locked.";
    }

    /// <summary>Exports a document's decrypted content to a caller-provided stream.</summary>
    public void ExportDocumentTo(string relativePath, Stream target)
    {
        session!.ReadFile(relativePath, target);
    }

    /// <summary>Exports a document's decrypted content to a file on the host.</summary>
    public void ExportDocumentTo(string relativePath, string targetPath)
    {
        using FileStream stream = File.Create(targetPath);
        ExportDocumentTo(relativePath, stream);
    }

    /// <summary>Per-session plaintext working folder for OPEN (removed on lock).</summary>
    public string EnsureOpenTempDirectory()
    {
        openTempDirectory ??= Path.Combine(Path.GetTempPath(), "EmergencyArchive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(openTempDirectory);
        return openTempDirectory;
    }

    /// <summary>Spec section 11/23: opening on the host leaves traces — say so.</summary>
    public void NoteExternalOpen(string name)
    {
        StatusMessage = $"Opened '{name}'. Opening a document can leave traces on this computer (temporary files, recent-file lists).";
    }

    // --- Internals ----------------------------------------------------------

    private void LoadDocuments()
    {
        Documents.Clear();
        foreach (string path in session!.EnumerateFiles().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            Documents.Add(new DocumentItemViewModel(path));
        }

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        string? term = SearchText?.Trim();
        FilteredDocuments.Clear();
        foreach (DocumentItemViewModel document in Documents)
        {
            if (string.IsNullOrEmpty(term) || document.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredDocuments.Add(document);
            }
        }
    }

    private async Task RunCooldownAsync(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        IsCooldownActive = true;
        try
        {
            while (remaining > TimeSpan.Zero)
            {
                StatusMessage = $"{WrongPasswordMessage}\nPlease wait {Math.Ceiling(remaining.TotalSeconds)} second(s) before trying again.";
                await Task.Delay(1000);
                remaining -= TimeSpan.FromSeconds(1);
            }

            StatusMessage = WrongPasswordMessage;
        }
        finally
        {
            IsCooldownActive = false;
        }
    }

    private void CleanupTempExports()
    {
        if (openTempDirectory is null)
        {
            return;
        }

        try
        {
            Directory.Delete(openTempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // The file may still be open in an external application; leave it.
        }
        catch (UnauthorizedAccessException)
        {
        }

        openTempDirectory = null;
    }
}
