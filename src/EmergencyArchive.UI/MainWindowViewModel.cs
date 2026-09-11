using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

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
    private OperationLog? operationLog;

    public MainWindowViewModel()
    {
        // Route sanitized diagnostics into the encrypted activity log while a
        // vault is unlocked (full detail still goes to trace via AppLog).
        AppLog.ActivitySink = RecordDiagnostic;

        // Decide the first screen: create-archive (no vault yet) or unlock.
        DetectVault();
    }

    /// <summary>
    /// Appends a sanitized diagnostic entry to the encrypted activity log, when
    /// a vault is unlocked. The caller (AppLog) has already stripped the entry
    /// to a category + exception type, so nothing sensitive is persisted
    /// (spec section 20). No-ops when locked (no log to write to).
    /// </summary>
    private void RecordDiagnostic(string category, string message)
    {
        if (session is null || operationLog is null)
        {
            return;
        }

        operationLog.Append(category, message);
        OperationLogStore.Save(session, operationLog);
    }

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
    [NotifyCanExecuteChangedFor(nameof(CreateArchiveCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool isCooldownActive;

    [ObservableProperty] private string? statusMessage;

    // --- Archive screen state -----------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterSetupCommand))]
    [NotifyPropertyChangedFor(nameof(IsCreating))]
    [NotifyPropertyChangedFor(nameof(IsUnlockable))]
    [NotifyPropertyChangedFor(nameof(IsBrowsing))]
    private bool isUnlocked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterSetupCommand))]
    private bool isIndexing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowsing))]
    [NotifyPropertyChangedFor(nameof(IsSetupCards))]
    private bool isSetupMode;

    /// <summary>Credential viewer (a sub-screen of Setup Mode).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetupCards))]
    private bool isCredentialsMode;

    public CredentialViewModel? Credentials { get; private set; }

    /// <summary>Browse screen is visible while unlocked and not in Setup Mode.</summary>
    public bool IsBrowsing => IsUnlocked && !IsSetupMode;

    /// <summary>The Setup cards show while in Setup but not viewing credentials.</summary>
    public bool IsSetupCards => IsSetupMode && !IsCredentialsMode;

    public SetupViewModel? Setup => setup;

    [ObservableProperty] private string? searchText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDocument))]
    private DocumentItemViewModel? selectedDocument;

    public ObservableCollection<DocumentItemViewModel> Documents { get; } = new();

    public ObservableCollection<DocumentItemViewModel> FilteredDocuments { get; } = new();

    /// <summary>Folder sidebar: "All documents" plus one entry per top-level folder (spec section 10).</summary>
    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();

    [ObservableProperty]
    private CategoryItemViewModel? selectedCategory;

    /// <summary>One-line summary of what the list is showing (e.g. "Showing 12 of 2,950 documents").</summary>
    [ObservableProperty]
    private string? resultsSummary;

    /// <summary>Search-index progress and outcome, shown beside the status line (not in it).</summary>
    [ObservableProperty]
    private string? indexStatusText;

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
            operationLog = OperationLogStore.Load(session);
            operationLog.Append("Vault", "Archive unlocked.");
            OperationLogStore.Save(session, operationLog);
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
        catch (VaultException e)
        {
            // Corrupt, tampered, or unsupported vault: never reveal details to
            // the UI, but log for diagnostics (goes to trace, not the vault).
            AppLog.Handled("UnlockAsync (vault integrity/format)", e);
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
        string? activeCategory = SelectedCategory is null || SelectedCategory.IsAll
            ? null
            : SelectedCategory.Name;

        int shown = 0;
        foreach (SearchResultItem result in results)
        {
            var item = new DocumentItemViewModel(result.RelativePath) { Snippet = result.Snippet };
            if (activeCategory is not null &&
                !string.Equals(item.Category, activeCategory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredDocuments.Add(item);
            shown++;
        }

        if (FilteredDocuments.Count > 0)
        {
            SelectedDocument = FilteredDocuments[0];
        }

        // Spec section 20: log the event, not the query text.
        operationLog?.Append("Search", $"Search performed: {results.Count} result(s).");
        if (session is not null && operationLog is not null)
        {
            OperationLogStore.Save(session, operationLog);
        }

        ResultsSummary = shown == results.Count
            ? $"{results.Count:N0} content match(es) for \"{query}\""
            : $"{shown:N0} of {results.Count:N0} content match(es) for \"{query}\" (folder filter active)";
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
                IndexStatusText = $"Preparing search index… {p.Processed:N0}/{p.Total:N0}");
            VaultSearchIndex index = await Task.Run(() => VaultSearchIndex.LoadOrBuild(current, progress));

            if (!ReferenceEquals(session, current))
            {
                index.Dispose(); // the vault was locked while indexing
                return;
            }

            searchIndex = index;
            IndexStatusText = $"Search ready — {index.DocumentCount:N0} indexed";
        }
        catch (Exception e) when (e is VaultException or ObjectDisposedException)
        {
            // ObjectDisposedException is expected if the vault was locked mid-index;
            // a VaultException means the index could not be built — record it.
            AppLog.Handled("IndexInBackground", e);
            searchIndex = null;
            if (IsUnlocked)
            {
                IndexStatusText = "Search unavailable — documents can still be browsed by name.";
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
            operationLog!,
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
        CloseCredentials();
        IsSetupMode = false;
        setup = null;
        LoadDocuments();
        SearchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Opens the in-app password manager viewer (native Avalonia) as a sub-screen
    /// of Setup. Loads the credential database from the encrypted vault; nothing
    /// is written to disk. Every step is recorded to the activity log.
    /// </summary>
    [RelayCommand]
    public void OpenCredentials()
    {
        if (session is null)
        {
            AppLog.Handled(
                "OpenCredentials",
                new InvalidOperationException("No unlocked vault session."));
            return;
        }

        operationLog?.Append("Credentials", "Opening password manager.");
        try
        {
            Credentials = new CredentialViewModel(session);
            OnPropertyChanged(nameof(Credentials));
            IsCredentialsMode = true;
            operationLog?.Append("Credentials", "Password manager opened.");
        }
        catch (Exception e) when (e is VaultException or IOException)
        {
            // Surface the failure to the activity log rather than swallowing it;
            // the viewer simply does not open.
            AppLog.Handled("OpenCredentials", e);
            operationLog?.Append("Credentials", "Password manager could not be opened.");
            Credentials = null;
            OnPropertyChanged(nameof(Credentials));
            IsCredentialsMode = false;
        }
    }

    /// <summary>Closes the credential viewer and returns to the Setup cards.</summary>
    [RelayCommand]
    public void CloseCredentials()
    {
        if (!IsCredentialsMode && Credentials is null)
        {
            return;
        }

        IsCredentialsMode = false;
        Credentials = null;
        OnPropertyChanged(nameof(Credentials));
        operationLog?.Append("Credentials", "Password manager closed.");
    }

    private bool CanEnterSetup => IsUnlocked && !IsBusy && !IsIndexing;

    /// <summary>Locks the archive (also invoked when the window closes, spec section 21).</summary>
    [RelayCommand]
    public void Lock()
    {
        IsCredentialsMode = false;
        Credentials = null;
        OnPropertyChanged(nameof(Credentials));
        IsSetupMode = false;
        setup = null;
        CleanupTempExports();
        operationLog?.Append("Vault", "Archive locked.");
        if (session is not null && operationLog is not null)
        {
            OperationLogStore.Save(session, operationLog);
        }

        session?.Dispose();
        session = null;
        searchIndex?.Dispose();
        searchIndex = null;
        operationLog = null;
        Documents.Clear();
        FilteredDocuments.Clear();
        Categories.Clear();
        SelectedCategory = null;
        ResultsSummary = null;
        IndexStatusText = null;
        SelectedDocument = null;
        SearchText = null;
        // Clear any password text still held in the entry boxes so it does not
        // linger (even obfuscated) after locking (spec section 19/21).
        Password = null;
        NewArchivePassword = null;
        ConfirmArchivePassword = null;
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

    /// <summary>
    /// Builds a safe temp path for OPEN from a document's (decrypted) name.
    /// Defense in depth: the name comes from vault content, so it is reduced to
    /// a bare, sanitized leaf and the resolved path is confirmed to stay inside
    /// the per-session temp directory — a hostile name (traversal, absolute
    /// path, reserved device name) can never cause a write outside it.
    /// </summary>
    public string BuildSafeOpenTargetPath(string documentName)
    {
        string tempDirectory = EnsureOpenTempDirectory();
        string safeName = SanitizeLeafFileName(documentName);
        string targetPath = Path.GetFullPath(Path.Combine(tempDirectory, safeName));

        string root = Path.GetFullPath(tempDirectory) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(root, StringComparison.Ordinal))
        {
            // Should be unreachable after sanitizing, but never write outside temp.
            throw new InvalidOperationException("Refusing to open a document outside the working directory.");
        }

        return targetPath;
    }

    /// <summary>Reduces any string to a single safe filename leaf for host writes.</summary>
    public static string SanitizeLeafFileName(string name)
    {
        // Strip any directory components a hostile name might carry.
        string leaf = Path.GetFileName(name.Replace('\\', '/').TrimEnd('/', '\\'));

        // Replace characters illegal on ANY target filesystem, not just the
        // one we happen to run on: the app targets Windows, but this logic is
        // also exercised on Linux (tests/CI). Windows forbids \ / : * ? " < > |
        // and control chars; we strip the union so behaviour is deterministic.
        var sanitized = new System.Text.StringBuilder(leaf.Length);
        foreach (char c in leaf)
        {
            sanitized.Append(c < 32 || "\\/:*?\"<>|".IndexOf(c) >= 0 ? '_' : c);
        }

        leaf = sanitized.ToString();

        leaf = leaf.Trim().Trim('.'); // no trailing dots/spaces (Windows quirk)

        if (string.IsNullOrEmpty(leaf))
        {
            return "document";
        }

        // Avoid Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9).
        string stem = Path.GetFileNameWithoutExtension(leaf);
        if (ReservedDeviceNames.Contains(stem))
        {
            leaf = "_" + leaf;
        }

        return leaf.Length > 200 ? leaf[^200..] : leaf;
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Spec section 11/23: opening on the host leaves traces — say so.</summary>
    public void NoteExternalOpen(string name)
    {
        StatusMessage = $"Opened '{name}'. Opening a document can leave traces on this computer (temporary files, recent-file lists).";
        operationLog?.Append("Documents", $"Document opened on host: {name} (may leave traces on this computer).");
        if (session is not null && operationLog is not null)
        {
            OperationLogStore.Save(session, operationLog);
        }
    }

    /// <summary>
    /// Records that a credential value was copied to the clipboard. Only the
    /// field label is logged — never the secret value itself.
    /// </summary>
    public void NoteCredentialCopied(string label)
    {
        operationLog?.Append("Credentials", $"Copied '{label}' to the clipboard.");
        if (session is not null && operationLog is not null)
        {
            OperationLogStore.Save(session, operationLog);
        }
    }

    /// <summary>Spec section 11: export events are recorded (the log itself is encrypted).</summary>
    public void NoteExport(string name)
    {
        operationLog?.Append("Documents", $"Document exported: {name}.");
        if (session is not null && operationLog is not null)
        {
            OperationLogStore.Save(session, operationLog);
        }
    }

    // --- Internals ----------------------------------------------------------

    private void LoadDocuments()
    {
        Documents.Clear();

        // Never show the vault's own infrastructure files (search index,
        // operations.log, sources.json, manifest) as documents — they are not
        // user content and only confuse browsing and search. This mirrors the
        // exclusion the search index already applies (VaultPaths).
        IEnumerable<string> documentPaths = session!.EnumerateFiles()
            .Where(path => !VaultPaths.IsInfrastructurePath(path))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        foreach (string path in documentPaths)
        {
            Documents.Add(new DocumentItemViewModel(path));
        }

        RebuildCategories();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(CategoryItemViewModel? value) => ApplyFilter();

    /// <summary>Rebuilds the folder sidebar, preserving the active folder when possible.</summary>
    private void RebuildCategories()
    {
        string? previous = SelectedCategory?.Name;
        Categories.Clear();

        Categories.Add(new CategoryItemViewModel(CategoryItemViewModel.AllName, Documents.Count));
        foreach (var group in Documents
            .GroupBy(d => d.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Categories.Add(new CategoryItemViewModel(group.Key, group.Count()));
        }

        SelectedCategory = Categories.FirstOrDefault(c => c.Name == previous) ?? Categories[0];
    }

    private void ApplyFilter()
    {
        string? term = SearchText?.Trim();
        string? category = SelectedCategory is null || SelectedCategory.IsAll
            ? null
            : SelectedCategory.Name;

        FilteredDocuments.Clear();
        int shown = 0;
        foreach (DocumentItemViewModel document in Documents)
        {
            if (category is not null &&
                !string.Equals(document.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(term) &&
                !document.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredDocuments.Add(document);
            shown++;
        }

        SelectedDocument = FilteredDocuments.Count > 0 ? FilteredDocuments[0] : null;
        ResultsSummary = term is null && category is null
            ? $"{Documents.Count:N0} document(s)"
            : $"Showing {shown:N0} of {Documents.Count:N0} document(s)";
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The file may still be open in an external application; we cannot
            // force-delete it. This is security-relevant (plaintext may remain
            // on the host), so record it for diagnostics rather than swallowing.
            AppLog.Handled("CleanupTempExports (temp plaintext may remain on host)", e);
        }

        openTempDirectory = null;
    }
}
