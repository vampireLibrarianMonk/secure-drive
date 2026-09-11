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

    [AvaloniaFact]
    public void CredentialsScreen_IsHiddenByDefault_AndVisibleWhenOpened()
    {
        var vm = new MainWindowViewModel { IsFirstRun = false, IsUnlocked = true, IsSetupMode = true };
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.False(ScreenVisible(window, "CredentialsScreen"));
        // The Setup cards are visible while credentials are closed.
        Assert.True(vm.IsSetupCards);

        vm.IsCredentialsMode = true;
        Assert.True(ScreenVisible(window, "CredentialsScreen"));
        Assert.False(vm.IsSetupCards);
        // Still exactly the Setup screen among the four named top-level screens.
        Assert.True(ScreenVisible(window, "SetupScreen"));
    }

    private static bool ScreenVisible(MainWindow window, string name)
    {
        Control control = window.GetLogicalDescendants()
            .OfType<Control>()
            .Single(c => c.Name == name);
        return control.IsVisible;
    }
}
