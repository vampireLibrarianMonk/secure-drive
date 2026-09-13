using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;
using EmergencyArchive.UI;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Renders the REAL credential viewer screen inside the real MainWindow at a
/// fixed window width and asserts the card content does not extend past the
/// window's right edge (the reported right-side clipping of the Delete/Copy
/// buttons).
/// </summary>
public sealed class CredentialCardLayoutTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;

    public CredentialCardLayoutTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-cardlayout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose()
    {
        try { Directory.Delete(vaultDir, recursive: true); }
        catch (IOException) { }
    }

    [AvaloniaFact]
    public void CredentialsScreen_CardsFitWithinTheWindow()
    {
        const double windowWidth = 900;

        // Seed a couple of credentials and build a real viewer over a real vault.
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        CredentialStore.Save(session, CredentialDatabase.Empty
            .With(new Credential("id1", "USAA", "pmflani@gmail.com", "supersecret",
                [new CredentialField("URL", "https://www.usaa.com/logon")]))
            .With(new Credential("id2", "NFCU", "flanigpm401", "anothersecret", [])));
        var credentials = new CredentialViewModel(session);

        var vm = new MainWindowViewModel { IsFirstRun = false, IsUnlocked = true, IsSetupMode = true };
        var window = new MainWindow { DataContext = vm, Width = windowWidth, Height = 700 };
        window.Show();

        // Inject the viewer + show the credentials screen (private setter in prod).
        typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.Credentials))!
            .SetValue(vm, credentials);
        vm.IsCredentialsMode = true;

        window.Measure(new Size(windowWidth, 700));
        window.Arrange(new Rect(0, 0, windowWidth, 700));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Control screen = window.GetLogicalDescendants().OfType<Control>()
            .Single(c => c.Name == "CredentialsScreen");

        // Every button on the credentials screen must sit within the window.
        Button[] buttons = screen.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsVisible && b.Bounds.Width > 0).ToArray();
        Assert.NotEmpty(buttons);

        foreach (Button b in buttons)
        {
            Point? tl = b.TranslatePoint(new Point(0, 0), window);
            if (tl is null)
            {
                continue;
            }

            double right = tl.Value.X + b.Bounds.Width;
            Assert.True(right <= windowWidth + 0.5,
                $"Button '{b.Content}' right edge {right:F1} exceeds window width {windowWidth}.");
        }
    }
}
