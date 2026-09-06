using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Search;

namespace EmergencyArchive.UI;

/// <summary>
/// Orchestrates the application screens: the emergency password screen
/// (spec section 7), the read-only archive screen, and Setup Mode (spec
/// section 12), including the 5 second rate limit between password attempts
/// (spec section 19).
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly AttemptRateLimiter rateLimiter = new();
    private VaultSession? session;
    private VaultSearchIndex? searchIndex;
    private string? vaultPath;
    private string? openTempDirectory;
    private SetupViewModel? setup;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterSetupCommand))]
    private bool isUnlocked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterSetupCommand))]
    private bool isIndexing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowsing))]
    private bool isSetupMode;

    /// <summary>Browse screen is visible while unlocked and not in Setup Mode.</summary>
    public bool IsBrowsing => IsUnlocked && !IsSetupMode;

    public SetupViewModel? Setup => setup;

    [ObservableProperty] private string? searchText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDocument))]
    private DocumentItemViewModel? selectedDocument;

    public ObservableCollection<DocumentItemViewModel> Documents { get; } = new();

    public ObservableCollection<DocumentItemViewModel> FilteredDocuments { get; } = new();

    public bool HasSelectedDocument => SelectedDocument is not null;

    private bool CanAttemptUnlock => !IsBusy && !IsCooldownActive && !string.IsNullOrEmpty(Password);

    private bool CanSearch => IsUnlocked && !IsIndexing && searchIndex is not null;

    // --- Commands -----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAttemptUnlock))]
    private async Task UnlockAsync()
    {
        // Enforce the 5 second interval on EVERY password entry (spec §19):
        // this covers quick re-entry after LOCK and retries after integrity
        // failures, not only wrong passwords. A blocked click does NOT restart
        // the window — spamming cannot extend the lock indefinitely.
        TimeSpan blockedFor = rateLimiter.RemainingDelay(DateTimeOffset.UtcNow);
        if (blockedFor > TimeSpan.Zero)
        {
            await RunCooldownAsync(blockedFor);
            return;
        }

        string? locatedVaultPath = VaultLocator.Locate();
        if (locatedVaultPath is null)
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
            VaultSession opened = await Task.Run(() => VaultStore.Unlock(locatedVaultPath, password));
            session = opened;
            vaultPath = locatedVaultPath;
            LoadDocuments();
            IsUnlocked = true;
            StatusMessage = UnlockedMessage;
            _ = IndexInBackgroundAsync(); // search becomes available once the index is ready
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

    /// <summary>Full-text search over document names and contents (spec section 8). Empty query shows all documents.</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search()
    {
        string query = SearchText?.Trim() ?? string.Empty;
        FilteredDocuments.Clear();
        SelectedDocument = null;

        if (query.Length == 0)
        {
            foreach (DocumentItemViewModel document in Documents)
            {
                FilteredDocuments.Add(document);
            }

            StatusMessage = $"Showing all {Documents.Count} document(s).";
            return;
        }

        IReadOnlyList<SearchResultItem> results = searchIndex!.Search(query);
        foreach (SearchResultItem result in results)
        {
            FilteredDocuments.Add(new DocumentItemViewModel(result.RelativePath) { Snippet = result.Snippet });
        }

        StatusMessage = results.Count == 0
            ? $"No matches for '{query}'."
            : $"{results.Count} matching document(s) for '{query}'.";
    }

    /// <summary>
    /// Loads or builds the FTS5 index in the background. While indexing runs,
    /// documents can still be browsed by name; search enables when ready.
    /// </summary>
    private async Task IndexInBackgroundAsync()
    {
        VaultSession? current = session;
        if (current is null)
        {
            return;
        }

        IsIndexing = true;
        try
        {
            var progress = new Progress<SearchIndexProgress>(p =>
                StatusMessage = $"Preparing search index… {p.Processed}/{p.Total}: {p.CurrentName}");
            VaultSearchIndex index = await Task.Run(() => VaultSearchIndex.LoadOrBuild(current, progress));

            if (!ReferenceEquals(session, current))
            {
                index.Dispose(); // the vault was locked while indexing
                return;
            }

            searchIndex = index;
            StatusMessage = $"Search ready — {index.DocumentCount} document(s) indexed.";
        }
        catch (Exception e) when (e is VaultException or ObjectDisposedException)
        {
            searchIndex = null;
            if (IsUnlocked)
            {
                StatusMessage = "The search index could not be prepared. Documents can still be browsed by name.";
            }
        }
        finally
        {
            IsIndexing = false;
        }
    }

    /// <summary>Enters Setup Mode (spec section 12) after normal authentication.</summary>
    [RelayCommand(CanExecute = nameof(CanEnterSetup))]
    private void EnterSetup()
    {
        if (session is null)
        {
            return;
        }

        var setupViewModel = new SetupViewModel(
            session,
            vaultPath!,
            () => searchIndex,
            newIndex => searchIndex = newIndex);
        setupViewModel.DocumentsChanged += (_, _) => Dispatcher.UIThread.Post(LoadDocuments);

        setup = setupViewModel;
        OnPropertyChanged(nameof(Setup));
        IsSetupMode = true;
    }

    /// <summary>Leaves Setup Mode and refreshes the browse screen.</summary>
    [RelayCommand]
    public void ExitSetup()
    {
        IsSetupMode = false;
        setup = null;
        LoadDocuments();
        SearchCommand.NotifyCanExecuteChanged();
    }

    private bool CanEnterSetup => IsUnlocked && !IsBusy && !IsIndexing;

    /// <summary>Locks the archive (also invoked when the window closes, spec section 21).</summary>
    [RelayCommand]
    public void Lock()
    {
        IsSetupMode = false;
        CleanupTempExports();
        session?.Dispose();
        session = null;
        searchIndex?.Dispose();
        searchIndex = null;
        setup = null;
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
