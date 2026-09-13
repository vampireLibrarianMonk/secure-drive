using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;
using EmergencyArchive.UI;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Tests for the native (no WebView2) in-app credential viewer: the view model
/// (search / reveal / obfuscation / extras) and that the viewer sub-screen
/// renders inside Setup without breaking the one-screen-visible invariant.
/// </summary>
public sealed class CredentialViewerTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private const string Mask = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
    private readonly string vaultDir;

    public CredentialViewerTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-credview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(vaultDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp vault.
        }
    }

    private CredentialViewModel LoadViewer(CredentialDatabase db)
    {
        using (VaultSession save = VaultStore.Unlock(vaultDir, Password))
        {
            CredentialStore.Save(save, db);
        }

        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        return new CredentialViewModel(session);
    }

    [Fact]
    public void NewVault_ViewerIsEmpty()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);

        var viewer = new CredentialViewModel(session);

        Assert.True(viewer.IsEmpty);
        Assert.Empty(viewer.Items);
    }

    [Fact]
    public void Load_ShowsAllEntriesSortedBySite()
    {
        CredentialDatabase db = CredentialDatabase.Empty
            .With(Credential.Create("zebra.example", "z@example.com", "zpass"))
            .With(Credential.Create("alpha.example", "a@example.com", "apass"));

        CredentialViewModel viewer = LoadViewer(db);

        Assert.Equal(2, viewer.Items.Count);
        Assert.Equal("alpha.example", viewer.Items[0].Site);
        Assert.Equal("zebra.example", viewer.Items[1].Site);
    }

    [Fact]
    public void SecretValues_AreObfuscatedByDefault()
    {
        CredentialDatabase db = CredentialDatabase.Empty
            .With(Credential.Create("bank.example", "user", "SUPERSECRET"));

        CredentialViewModel viewer = LoadViewer(db);

        SecretFieldViewModel password = viewer.Items[0].Password;
        Assert.False(password.IsRevealed);
        Assert.Equal(Mask, password.Display);
        Assert.DoesNotContain("SUPERSECRET", password.Display);
        // The real value is still available for copy even while hidden.
        Assert.Equal("SUPERSECRET", password.CopyValue);
    }

    [Fact]
    public void Reveal_ShowsTheRealValue_AndTogglesBack()
    {
        CredentialDatabase db = CredentialDatabase.Empty
            .With(Credential.Create("bank.example", "user", "SUPERSECRET"));

        SecretFieldViewModel password = LoadViewer(db).Items[0].Password;

        password.IsRevealed = true;
        Assert.Equal("SUPERSECRET", password.Display);
        Assert.Equal("Hide", password.RevealButtonText);

        password.IsRevealed = false;
        Assert.Equal(Mask, password.Display);
        Assert.Equal("Reveal", password.RevealButtonText);
    }

    [Fact]
    public void Search_FiltersBySiteOrUsername_CaseInsensitive()
    {
        CredentialDatabase db = CredentialDatabase.Empty
            .With(Credential.Create("bank.example", "alice", "p1"))
            .With(Credential.Create("mail.example", "bob", "p2"));

        CredentialViewModel viewer = LoadViewer(db);

        viewer.SearchText = "BANK";
        Assert.Single(viewer.Items);
        Assert.Equal("bank.example", viewer.Items[0].Site);

        viewer.SearchText = "bob";
        Assert.Single(viewer.Items);
        Assert.Equal("mail.example", viewer.Items[0].Site);

        viewer.SearchText = "";
        Assert.Equal(2, viewer.Items.Count);
    }

    [Fact]
    public void Extras_AreExposedAsObfuscatedFields()
    {
        var withExtras = new Credential(
            Guid.NewGuid().ToString("N"),
            "vault.example",
            "user",
            "pw",
            [new CredentialField("Recovery code", "RCODE-123"), new CredentialField("PIN", "4242")]);
        CredentialDatabase db = CredentialDatabase.Empty.With(withExtras);

        CredentialItemViewModel item = LoadViewer(db).Items[0];

        Assert.True(item.HasExtras);
        Assert.Equal(2, item.ExtrasCount);
        SecretFieldViewModel recovery = item.Extras[0];
        Assert.Equal("Recovery code", recovery.Label);
        Assert.Equal(Mask, recovery.Display);
        Assert.Equal("RCODE-123", recovery.CopyValue);
    }

    // --- CRUD ---------------------------------------------------------------

    [Fact]
    public void Add_PersistsAndReloads()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            var viewer = new CredentialViewModel(session);
            viewer.AddCommand.Execute(null);
            Assert.True(viewer.IsEditing);

            viewer.Editor!.Site = "bank.example";
            viewer.Editor.Username = "alice";
            viewer.Editor.Password = "s3cret";
            viewer.SaveCommand.Execute(null);

            Assert.False(viewer.IsEditing);
            Assert.Single(viewer.Items);
        }

        // Reload from a fresh session: the entry survived (encrypted on disk).
        using (VaultSession reload = VaultStore.Unlock(vaultDir, Password))
        {
            var reloaded = new CredentialViewModel(reload);
            Assert.Single(reloaded.Items);
            Assert.Equal("bank.example", reloaded.Items[0].Site);
            Assert.Equal("alice", reloaded.Items[0].Username);
            Assert.Equal("s3cret", reloaded.Items[0].Password.CopyValue);
        }
    }

    [Fact]
    public void Add_WithBlankSite_DoesNotSave()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        var viewer = new CredentialViewModel(session);

        viewer.AddCommand.Execute(null);
        viewer.Editor!.Site = "   ";
        viewer.SaveCommand.Execute(null);

        Assert.True(viewer.IsEditing);              // still on the form
        Assert.True(viewer.Editor.HasError);
        Assert.Empty(viewer.Items);
    }

    [Fact]
    public void Edit_UpdatesInPlace_AndPersists()
    {
        CredentialDatabase db = CredentialDatabase.Empty
            .With(Credential.Create("old.example", "user", "oldpw"));

        string id;
        using (VaultSession save = VaultStore.Unlock(vaultDir, Password))
        {
            CredentialStore.Save(save, db);
        }

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            var viewer = new CredentialViewModel(session);
            CredentialItemViewModel row = viewer.Items[0];
            id = row.Id;

            viewer.EditCommand.Execute(row);
            Assert.False(viewer.Editor!.IsNew);
            viewer.Editor.Site = "new.example";
            viewer.Editor.Password = "newpw";
            viewer.SaveCommand.Execute(null);

            Assert.Single(viewer.Items);
            Assert.Equal("new.example", viewer.Items[0].Site);
            Assert.Equal(id, viewer.Items[0].Id);   // same identity, replaced in place
        }

        using (VaultSession reload = VaultStore.Unlock(vaultDir, Password))
        {
            var reloaded = new CredentialViewModel(reload);
            Assert.Single(reloaded.Items);
            Assert.Equal("new.example", reloaded.Items[0].Site);
            Assert.Equal("newpw", reloaded.Items[0].Password.CopyValue);
        }
    }

    [Fact]
    public void Delete_RemovesAndPersists()
    {
        CredentialDatabase db = CredentialDatabase.Empty
            .With(Credential.Create("a.example", "u1", "p1"))
            .With(Credential.Create("b.example", "u2", "p2"));

        using (VaultSession save = VaultStore.Unlock(vaultDir, Password))
        {
            CredentialStore.Save(save, db);
        }

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            var viewer = new CredentialViewModel(session);
            CredentialItemViewModel toDelete = viewer.Items.First(i => i.Site == "a.example");
            viewer.DeleteCommand.Execute(toDelete);

            Assert.Single(viewer.Items);
            Assert.Equal("b.example", viewer.Items[0].Site);
        }

        using (VaultSession reload = VaultStore.Unlock(vaultDir, Password))
        {
            var reloaded = new CredentialViewModel(reload);
            Assert.Single(reloaded.Items);
            Assert.Equal("b.example", reloaded.Items[0].Site);
        }
    }

    [Fact]
    public void Add_WithExtras_RoundTripsThroughStore()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            var viewer = new CredentialViewModel(session);
            viewer.AddCommand.Execute(null);
            viewer.Editor!.Site = "vault.example";
            viewer.Editor.AddExtraCommand.Execute(null);
            viewer.Editor.Extras[0].Key = "Recovery code";
            viewer.Editor.Extras[0].Value = "RC-999";
            // A blank-key extra row must be dropped on save.
            viewer.Editor.AddExtraCommand.Execute(null);
            viewer.SaveCommand.Execute(null);
        }

        using (VaultSession reload = VaultStore.Unlock(vaultDir, Password))
        {
            var reloaded = new CredentialViewModel(reload);
            CredentialItemViewModel item = reloaded.Items[0];
            Assert.Equal(1, item.ExtrasCount);
            Assert.Equal("Recovery code", item.Extras[0].Label);
            Assert.Equal("RC-999", item.Extras[0].CopyValue);
        }
    }

    // --- KeePass (KDBX) import / export -------------------------------------

    [Fact]
    public void BeginImport_ThenConfirm_MergesAndPersists()
    {
        // Produce a real .kdbx from two credentials.
        var source = CredentialDatabase.Empty
            .With(Credential.Create("bank.example", "alice", "pw1"))
            .With(Credential.Create("mail.example", "bob", "pw2"));
        byte[] kdbx = KdbxCredentialMapper.Export(source, "kdbx-pass");

        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            var viewer = new CredentialViewModel(session);
            viewer.BeginImport(kdbx);
            Assert.True(viewer.IsKdbxPrompt);

            viewer.KdbxPassword = "kdbx-pass";
            viewer.ConfirmKdbxCommand.Execute(null);

            Assert.False(viewer.IsKdbxPrompt);          // prompt closed on success
            Assert.Equal(2, viewer.Items.Count);
        }

        // Persisted through the vault.
        using (VaultSession reload = VaultStore.Unlock(vaultDir, Password))
        {
            var reloaded = new CredentialViewModel(reload);
            Assert.Equal(2, reloaded.Items.Count);
            Assert.Contains(reloaded.Items, i => i.Site == "bank.example");
        }
    }

    [Fact]
    public void Import_WrongKdbxPassword_ShowsError_AndDoesNotImport()
    {
        byte[] kdbx = KdbxCredentialMapper.Export(
            CredentialDatabase.Empty.With(Credential.Create("s", "u", "p")), "right-pass");

        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        var viewer = new CredentialViewModel(session);
        viewer.BeginImport(kdbx);
        viewer.KdbxPassword = "wrong-pass";
        viewer.ConfirmKdbxCommand.Execute(null);

        Assert.True(viewer.IsKdbxPrompt);   // stays open on error
        Assert.True(viewer.HasKdbxError);
        Assert.Empty(viewer.Items);
    }

    [Fact]
    public void BeginExport_ThenConfirm_WritesBytesThroughTheView()
    {
        var db = CredentialDatabase.Empty.With(Credential.Create("bank.example", "alice", "pw1"));

        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        var viewer = new CredentialViewModel(session);
        // seed the store so export has content
        CredentialStore.Save(session, db);
        var seeded = new CredentialViewModel(session);

        byte[]? written = null;
        seeded.BeginExport(bytes => { written = bytes; return Task.CompletedTask; });
        Assert.True(seeded.IsKdbxPrompt);

        seeded.KdbxPassword = "export-pass";
        seeded.ConfirmKdbxCommand.Execute(null);

        Assert.False(seeded.IsKdbxPrompt);
        Assert.NotNull(written);
        // The written bytes are a valid KDBX openable with the same password.
        IReadOnlyList<Credential> back = KdbxCredentialMapper.Import(written!, "export-pass");
        Assert.Contains(back, c => c.Site == "bank.example");
    }

    [Fact]
    public void CancelKdbx_ClosesThePrompt()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        var viewer = new CredentialViewModel(session);
        viewer.BeginImport([1, 2, 3]);
        Assert.True(viewer.IsKdbxPrompt);

        viewer.CancelKdbxCommand.Execute(null);
        Assert.False(viewer.IsKdbxPrompt);
    }

    [AvaloniaFact]
    public void CredentialsScreen_IsHiddenByDefault_AndVisibleWhenOpened()
    {
        var vm = new MainWindowViewModel { IsFirstRun = false, IsUnlocked = true, IsSetupMode = true };
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.False(ScreenVisible(window, "CredentialsScreen"));
        // The Setup content (header + cards + activity log) shows while closed.
        Assert.True(vm.IsSetupCards);
        Assert.True(ScreenVisible(window, "SetupContent"));

        vm.IsCredentialsMode = true;
        Assert.True(ScreenVisible(window, "CredentialsScreen"));
        Assert.False(vm.IsSetupCards);
        // The whole Setup panel (including the activity log) must hide so it
        // cannot overlap or block the credential viewer.
        Assert.False(ScreenVisible(window, "SetupContent"));
        // Still the Setup screen among the four named top-level screens.
        Assert.True(ScreenVisible(window, "SetupScreen"));
    }

    [AvaloniaFact]
    public void OpenPasswordManagerButton_IsBoundToTheOpenCommand()
    {
        // Regression guard: the OPEN PASSWORD MANAGER button reaches the root
        // view model's command via ElementName. A compiled ElementName binding
        // silently fails to resolve, leaving the button with no command so
        // clicking it does nothing — which is exactly "can't get to the
        // password manager". Assert the binding actually resolved.
        var vm = new MainWindowViewModel { IsFirstRun = false, IsUnlocked = true, IsSetupMode = true };
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Button open = FindButtonByContent(window, "OPEN PASSWORD MANAGER");
        Assert.NotNull(open.Command);
        Assert.Same(vm.OpenCredentialsCommand, open.Command);

        // The CLOSE button uses the same pattern and must also resolve.
        Button close = FindButtonByContent(window, "CLOSE");
        Assert.Same(vm.CloseCredentialsCommand, close.Command);
    }

    private static Button FindButtonByContent(MainWindow window, string content) =>
        window.GetLogicalDescendants()
            .OfType<Button>()
            .Single(b => b.Content as string == content);

    private static bool ScreenVisible(MainWindow window, string name)
    {
        Control control = window.GetLogicalDescendants()
            .OfType<Control>()
            .Single(c => c.Name == name);
        return control.IsVisible;
    }
}
