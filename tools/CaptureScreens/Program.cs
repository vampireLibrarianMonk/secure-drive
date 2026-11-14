using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Sync;
using EmergencyArchive.UI;

// Headless screenshot capture (red/blue documentation cycle, 2026-11):
// drives the REAL Emergency Archive app through the states documented in
// docs/USER-GUIDE.md and captures PNG frames into reference_images/ using the
// slugs the guides already reference. All seeded data is FAKE sample content
// (no real names, numbers, or secrets), so the images are safe to commit.
//
// Usage:
//   dotnet run --project tools/CaptureScreens [-- <output-dir>]
//   (default output: reference_images; run inside the pinned SDK container
//    on machines without a host SDK)

internal static class Program
{
    private const string Password = "correct horse battery staple";

    [STAThread]
    public static int Main(string[] args)
    {
        string outputDir = args.Length > 0 ? args[0] : "reference_images";
        Directory.CreateDirectory(outputDir);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .LogToTrace()
            .SetupWithoutStarting();

        // --- scratch vault with fake sample documents -----------------------
        string vaultDir = Path.Combine(Path.GetTempPath(), $"ea-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        Environment.SetEnvironmentVariable(VaultLocator.VaultPathEnvironmentVariable, vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);

        SeedDocuments(vaultDir);

        // --- state 1: CREATE YOUR ARCHIVE (fresh drive, no vault yet) -------
        string emptyDir = Path.Combine(Path.GetTempPath(), $"ea-capture-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(emptyDir, "public"));
        Environment.SetEnvironmentVariable(VaultLocator.VaultPathEnvironmentVariable, emptyDir);
        CaptureState(outputDir, "create-archive-screen", vm => vm, afterShow: null);

        // --- state 2..n: real vault -----------------------------------------
        Environment.SetEnvironmentVariable(VaultLocator.VaultPathEnvironmentVariable, vaultDir);

        // The full unlock flow runs off the UI thread (background continuations
        // have no synchronization context there), then the window binds to the
        // fully-populated view model.
        MainWindowViewModel unlocked = RunInBackground(() =>
        {
            var vm = new MainWindowViewModel { Password = Password };
            vm.UnlockCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            // Wait for the background search index to finish building (can be
            // slow through the container file share).
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(180);
            while (vm.IsIndexing && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }

            return vm;
        });

        // Hero capture FIRST: the browse screen must be captured before
        // entering Setup Mode (IsSetupMode changes the whole window).
        CaptureState(outputDir, "hero-main-screen", _ => unlocked, afterShow: null);

        // Search results with excerpts ("insurance" matches the sample policy).
        CaptureState(outputDir, "search-results", _ =>
        {
            unlocked.SearchText = "insurance";
            for (int attempt = 0; attempt < 5 && !unlocked.SearchCommand.CanExecute(null); attempt++)
            {
                Thread.Sleep(500);
            }
            unlocked.SearchCommand.Execute(null);
            return unlocked;
        }, afterShow: null);

        // Enter Setup with retries: CanEnterSetup requires the background index
        // to have finished, so poll until the Setup view model appears.
        var setupDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(180);
        while (unlocked.Setup is null && DateTime.UtcNow < setupDeadline)
        {
            unlocked.EnterSetupCommand.Execute(null);
            if (unlocked.Setup is null)
            {
                Thread.Sleep(250);
            }
        }

        if (unlocked.Setup is null)
        {
            Console.WriteLine("FAIL: Setup mode could not be entered (index never became ready).");
            return 1;
        }

        CaptureState(outputDir, "setup-overview", _ => unlocked, afterShow: null);

        // Estate planning card with the filled-in letter template.
        CaptureState(outputDir, "estate-planning-letter", _ =>
        {
            unlocked.EnterSetupCommand.Execute(null);
            var setup = unlocked.Setup!;
            setup.EstateOwnerName = "Alex Doe";
            setup.EstateContact = "sister@example.org";
            setup.GenerateTemplateCommand.Execute(null);
            return unlocked;
        }, afterShow: window => ScrollToCard(window, "1 · ESTATE PLANNING"));

        // Your documents card.
        CaptureState(outputDir, "add-documents", _ =>
        {
            unlocked.EnterSetupCommand.Execute(null);
            return unlocked;
        }, afterShow: window => ScrollToCard(window, "2 · YOUR DOCUMENTS"));

        // Manage documents card with a selected document.
        CaptureState(outputDir, "manage-documents", _ =>
        {
            unlocked.EnterSetupCommand.Execute(null);
            var setup = unlocked.Setup!;
            setup.SelectedDocumentPath = setup.ManageableDocuments
                .FirstOrDefault(p => p.EndsWith("Home-Policy.txt", StringComparison.Ordinal))
                ?? setup.ManageableDocuments.FirstOrDefault();
            return unlocked;
        }, afterShow: window => ScrollToCard(window, "3 · MANAGE DOCUMENTS"));

        // Password manager viewer with seeded credentials.
        CaptureState(outputDir, "password-manager", _ =>
        {
            unlocked.EnterSetupCommand.Execute(null);
            unlocked.OpenCredentialsCommand.Execute(null);
            var viewer = unlocked.Credentials!;
            AddCredential(viewer, "bank.example", "alice", "s3cret-bank", "Branch", "Downtown 7");
            AddCredential(viewer, "mail.example", "bob", "s3cret-mail", "", "");
            return unlocked;
        }, afterShow: null);

        // Unlock screen: lock at the very end (clears the archive view).
        CaptureState(outputDir, "unlock-screen", _ =>
        {
            unlocked.CloseCredentialsCommand.Execute(null);
            unlocked.Lock();
            return unlocked;
        }, afterShow: null);

        Environment.SetEnvironmentVariable(VaultLocator.VaultPathEnvironmentVariable, null);
        unlocked.Lock();
        Console.WriteLine($"Screenshots written to {Path.GetFullPath(outputDir)}");
        return 0;
    }

    private static void CaptureState(string outputDir, string slug, Func<MainWindowViewModel, MainWindowViewModel> prepare, Action<Window>? afterShow)
    {
        MainWindowViewModel vm = prepare(new MainWindowViewModel());
        var window = new MainWindow { DataContext = vm, Width = 1150, Height = 780 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        afterShow?.Invoke(window);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.WriteLine($"SKIP {slug}: empty frame");
        }
        else
        {
            string path = Path.Combine(outputDir, slug + ".png");
            frame.Save(path);
            Console.WriteLine($"captured {slug}.png ({new FileInfo(path).Length:N0} bytes)");
        }

        // Hide (not Close): MainWindow.Closed locks the archive, which would
        // tear down the session between captures.
        window.Hide();
    }

    private static void ScrollToCard(Window window, string heading)
    {
        var headingBlock = window.GetLogicalDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == heading);
        if (headingBlock is null)
        {
            Console.WriteLine($"  scroll: heading '{heading}' not found");
            return;
        }

        var scrollViewer = headingBlock.GetLogicalAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            Console.WriteLine($"  scroll: no ScrollViewer above '{heading}'");
            return;
        }

        var position = headingBlock.TranslatePoint(new Point(0, 0), scrollViewer);
        if (position.HasValue)
        {
            double target = scrollViewer.Offset.Y + position.Value.Y - 24;
            scrollViewer.Offset = new Vector(0, Math.Max(0, target));
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static T RunInBackground<T>(Func<T> action)
    {
        var task = Task.Run(action);
        task.GetAwaiter().GetResult();
        return task.Result;
    }

    private static void SeedDocuments(string vaultDir)
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        var manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty(session.VaultId, "not committed");
        var entries = new List<ManifestEntry>(manifest.Entries);

        var samples = new (string Path, string Content)[]
        {
            ("Insurance/Home-Policy.txt", "HOME INSURANCE POLICY (SAMPLE)\nPolicy number SAMPLE-2026-0001\nCovers fire, water and liability.\n"),
            ("Identity/Passport.txt", "PASSPORT (SAMPLE DATA)\nNumber: XX0000000\nIssued by: Sample Authority\n"),
            ("Medical/Prescriptions.txt", "PRESCRIPTIONS (SAMPLE)\nExample medication, one per day.\nRenew every three months.\n"),
            ("Legal/Will-Summary.txt", "LAST WILL AND TESTAMENT (SAMPLE SUMMARY)\nThe executor is named on the contact sheet.\n"),
            ("Family/Contact-List.txt", "CONTACTS (SAMPLE)\nFamily doctor, plumber, insurance agent.\n"),
        };

        foreach (var (relativePath, content) in samples)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            session.WriteFile(relativePath, bytes);
            entries.RemoveAll(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            entries.Add(new ManifestEntry(relativePath, bytes.Length, DateTimeOffset.UtcNow,
                Sha256.ComputeHash(new MemoryStream(bytes))) { Source = ManifestSource.Manual });
        }

        ManifestStore.Save(session, manifest with { Entries = entries });
    }

    private static void AddCredential(CredentialViewModel viewer, string site, string username, string password, string extraKey, string extraValue)
    {
        viewer.AddCommand.Execute(null);
        if (viewer.Editor is null)
        {
            return;
        }

        viewer.Editor.Site = site;
        viewer.Editor.Username = username;
        viewer.Editor.Password = password;
        if (extraKey.Length > 0)
        {
            viewer.Editor.AddExtraCommand.Execute(null);
            viewer.Editor.Extras[0].Key = extraKey;
            viewer.Editor.Extras[0].Value = extraValue;
        }

        viewer.SaveCommand.Execute(null);
    }
}
