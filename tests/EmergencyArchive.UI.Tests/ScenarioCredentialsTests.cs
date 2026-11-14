using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Scenario: the owner's password manager. New credentials are added through
/// the in-app viewer (site, username, password, an extra field), survive
/// closing the viewer, locking, and re-unlocking, and CLEAR ALL purges every
/// credential — while the archived documents are provably untouched.
/// </summary>
[Collection("scenario")]
public sealed class ScenarioCredentialsTests : IDisposable
{
    private readonly string vaultDir = ScenarioSupport.NewVaultPath();
    private readonly string documentPath;

    public ScenarioCredentialsTests()
    {
        ScenarioSupport.SetVaultOverride(vaultDir);
        ScenarioSupport.CreateCheapVault(vaultDir, ScenarioSupport.Password);

        // One real document, so the "clearing does not touch documents" guarantee
        // can be verified against actual vault content.
        documentPath = Path.Combine(Path.GetTempPath(), $"emergencyarchive-doc-{Guid.NewGuid():N}.txt");
        File.WriteAllText(documentPath, "Home insurance policy number PXA-2026. Platypus umbrella rider.");
    }

    public void Dispose()
    {
        ScenarioSupport.SetVaultOverride(null);
        try
        {
            Directory.Delete(vaultDir, recursive: true);
            File.Delete(documentPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private static void AddCredential(CredentialViewModel viewer, string site, string username, string password, string extraKey, string extraValue)
    {
        viewer.AddCommand.Execute(null);
        Assert.True(viewer.IsEditing);
        viewer.Editor!.Site = site;
        viewer.Editor.Username = username;
        viewer.Editor.Password = password;
        if (extraKey.Length > 0)
        {
            viewer.Editor.AddExtraCommand.Execute(null);
            viewer.Editor.Extras[0].Key = extraKey;
            viewer.Editor.Extras[0].Value = extraValue;
        }

        viewer.SaveCommand.Execute(null);
        Assert.False(viewer.IsEditing);
    }

    [Fact]
    public async Task AddNewCredentials_ThroughTheMainJourney_AndPersistAcrossLockCycles()
    {
        var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await vm.UnlockCommand.ExecuteAsync(null);
        Assert.True(vm.IsUnlocked);

        vm.EnterSetupCommand.Execute(null);
        vm.OpenCredentialsCommand.Execute(null);
        Assert.NotNull(vm.Credentials);
        Assert.True(vm.Credentials!.IsEmpty);

        AddCredential(vm.Credentials, "bank.example", "alice", "s3cret-bank", "Branch", "Downtown 7");
        AddCredential(vm.Credentials, "mail.example", "bob", "s3cret-mail", "", "");
        Assert.Equal(2, vm.Credentials.Items.Count);

        vm.CloseCredentialsCommand.Execute(null);
        Assert.Null(vm.Credentials);
        vm.Lock();

        // Re-open in a fresh application instance: the store persisted.
        var again = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await again.UnlockCommand.ExecuteAsync(null);
        again.EnterSetupCommand.Execute(null);
        again.OpenCredentialsCommand.Execute(null);

        Assert.Equal(2, again.Credentials!.Items.Count);
        CredentialItemViewModel bank = again.Credentials.Items.Single(i => i.Site == "bank.example");
        Assert.Equal("alice", bank.Username);
        Assert.False(bank.Password.IsRevealed);                       // hidden by default
        Assert.DoesNotContain("s3cret-bank", bank.Password.Display);  // masked
        Assert.Equal("s3cret-bank", bank.Password.CopyValue);         // copy still works
        Assert.Equal("Downtown 7", bank.Extras[0].CopyValue);
        again.Lock();
    }

    [Fact]
    public async Task ClearAll_CancelledThenConfirmed_RemovesCredentials_ButKeepsDocuments()
    {
        var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await vm.UnlockCommand.ExecuteAsync(null);

        // Add a document through the owner flow (direct file add).
        vm.EnterSetupCommand.Execute(null);
        vm.Setup!.SelectedImportCategory = "Insurance";
        vm.Setup.AddFilesFromPaths([documentPath]);
        await ScenarioSupport.WaitAsync(
            () => vm.Setup.ManageableDocuments.Contains("Insurance/" + Path.GetFileName(documentPath)),
            TimeSpan.FromSeconds(20));

        // Add two credentials, then exercise CLEAR ALL: cancel first.
        vm.OpenCredentialsCommand.Execute(null);
        var viewer = vm.Credentials!;
        AddCredential(viewer, "a.example", "u1", "p1", "", "");
        AddCredential(viewer, "b.example", "u2", "p2", "", "");

        viewer.ClearAllCommand.Execute(null);
        Assert.True(viewer.IsConfirmingClearAll);
        viewer.CancelClearAllCommand.Execute(null);
        Assert.False(viewer.IsConfirmingClearAll);
        Assert.Equal(2, viewer.Items.Count);

        // Confirm: every credential is purged and the purge persists.
        viewer.ClearAllCommand.Execute(null);
        viewer.ConfirmClearAllCommand.Execute(null);
        Assert.Empty(viewer.Items);

        vm.CloseCredentialsCommand.Execute(null);
        vm.OpenCredentialsCommand.Execute(null);
        Assert.True(vm.Credentials!.IsEmpty);
        vm.CloseCredentialsCommand.Execute(null);

        // The archived document is untouched by the credential purge.
        vm.ExitSetupCommand.Execute(null);
        Assert.Contains(vm.Documents, d => d.Name.Contains(Path.GetFileName(documentPath), StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeePass_ExportThenImport_RoundTripsThroughTheViewer()
    {
        var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await vm.UnlockCommand.ExecuteAsync(null);

        vm.EnterSetupCommand.Execute(null);
        vm.OpenCredentialsCommand.Execute(null);
        var viewer = vm.Credentials!;
        AddCredential(viewer, "bank.example", "alice", "s3cret-bank", "", "");

        byte[]? exported = null;
        viewer.BeginExport(bytes =>
        {
            exported = bytes;
            return Task.CompletedTask;
        });
        viewer.KdbxPassword = "keeppass-file-pass";
        await viewer.ConfirmKdbxCommand.ExecuteAsync(null);
        Assert.False(viewer.IsKdbxPrompt);
        Assert.NotNull(exported);

        // Importing the same file adds a fresh-id copy of the entry.
        viewer.BeginImport(exported!);
        Assert.True(viewer.IsKdbxPrompt);
        viewer.KdbxPassword = "keeppass-file-pass";
        await viewer.ConfirmKdbxCommand.ExecuteAsync(null);

        Assert.False(viewer.IsKdbxPrompt);
        Assert.Equal(2, viewer.Items.Count);
        Assert.Equal(2, viewer.Items.Count(i => i.Site == "bank.example"));
        vm.Lock();
    }
}
