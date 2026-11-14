using System.Text;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Scenario: the main explanation document — the estate-planning letter. The
/// owner fills in their name and a contact, inserts the starter template,
/// edits it, saves it (landing in the vault's "Estate Plan" folder and in the
/// search index), the password-free companion lands in public\ on the drive,
/// re-opening Setup keeps the saved letter for further editing, and VERIFY
/// reports the archive HEALTHY afterwards.
/// </summary>
[Collection("scenario")]
public sealed class ScenarioEstateLetterTests : IDisposable
{
    private readonly string driveRoot = ScenarioSupport.NewDriveRoot();
    private readonly string vaultDir;

    public ScenarioEstateLetterTests()
    {
        vaultDir = Path.Combine(driveRoot, "vault");
        ScenarioSupport.SetVaultOverride(vaultDir);
        ScenarioSupport.CreateCheapVault(vaultDir, ScenarioSupport.Password);
    }

    public void Dispose()
    {
        ScenarioSupport.SetVaultOverride(null);
        try
        {
            Directory.Delete(driveRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private const string LetterVaultPath = "Estate Plan/READ-ME-FIRST — Letter to my family.txt";
    private const string PublicReadmePath = "public/ESTATE-PLAN-README.txt";

    [Fact]
    public async Task SaveLetter_WritesItIntoTheVault_IndexesIt_AndWritesThePublicReadme()
    {
        var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await vm.UnlockCommand.ExecuteAsync(null);
        await ScenarioSupport.WaitAsync(() => !vm.IsIndexing, TimeSpan.FromSeconds(30));

        vm.EnterSetupCommand.Execute(null);
        var setup = vm.Setup!;
        setup.EstateOwnerName = "Alex Doe";
        setup.EstateContact = "sister@example.org";

        setup.GenerateTemplateCommand.Execute(null);
        Assert.StartsWith("A LETTER TO MY FAMILY", setup.EstateLetterText, StringComparison.Ordinal);
        Assert.Contains("Alex Doe", setup.EstateLetterText, StringComparison.Ordinal);
        Assert.Contains("sister@example.org", setup.EstateLetterText, StringComparison.Ordinal);

        // The owner edits the template before saving.
        setup.EstateLetterText += "\nEXTRA NOTE FOR TESTS: the safe key is in the blue envelope.\n";
        await setup.SaveEstateLetterCommand.ExecuteAsync(null);

        Assert.Contains("saved", setup.EstateStatusChip, StringComparison.OrdinalIgnoreCase);

        // The letter is a real, encrypted vault document.
        Assert.Contains(setup.ManageableDocuments, p => p == LetterVaultPath);

        // The password-free companion was written next to the vault (drive public\).
        string publicReadme = Path.Combine(driveRoot, PublicReadmePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(publicReadme));
        string readme = await File.ReadAllTextAsync(publicReadme);
        Assert.Contains("Alex Doe", readme, StringComparison.Ordinal);
        Assert.Contains("sister@example.org", readme, StringComparison.Ordinal);

        // The letter is searchable immediately (indexed on save).
        vm.ExitSetupCommand.Execute(null);
        vm.SearchText = "blue envelope";
        vm.SearchCommand.Execute(null);
        Assert.Contains(vm.FilteredDocuments, d => d.RelativePath == LetterVaultPath);

        // The exported letter matches exactly what the owner saved.
        string letterOut = Path.Combine(Path.GetTempPath(), $"emergencyarchive-letter-{Guid.NewGuid():N}.txt");
        try
        {
            vm.ExportDocumentTo(LetterVaultPath, letterOut);
            string saved = await File.ReadAllTextAsync(letterOut, Encoding.UTF8);
            Assert.Contains("EXTRA NOTE FOR TESTS", saved, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(letterOut);
        }

        // The archive stays HEALTHY after the save.
        vm.EnterSetupCommand.Execute(null);
        await vm.Setup!.VerifyCommand.ExecuteAsync(null);
        Assert.StartsWith("HEALTHY", vm.Setup.IntegrityStatus, StringComparison.Ordinal);
        vm.Lock();
    }

    [Fact]
    public async Task ReopenSetup_KeepsTheSavedLetter_ForFurtherEditing()
    {
        var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await vm.UnlockCommand.ExecuteAsync(null);

        vm.EnterSetupCommand.Execute(null);
        var setup = vm.Setup!;
        setup.EstateOwnerName = "Alex Doe";
        setup.EstateContact = "sister@example.org";
        setup.GenerateTemplateCommand.Execute(null);
        await setup.SaveEstateLetterCommand.ExecuteAsync(null);

        // Leave and re-enter Setup: the saved letter is loaded, not a fresh template.
        vm.ExitSetupCommand.Execute(null);
        vm.EnterSetupCommand.Execute(null);
        var reopened = vm.Setup!;
        Assert.Contains("Alex Doe", reopened.EstateLetterText, StringComparison.Ordinal);
        Assert.Contains("saved — edit and save again to update", reopened.EstateStatusChip, StringComparison.Ordinal);

        // Further edits update the same document in place.
        reopened.EstateLetterText = reopened.EstateLetterText.Replace(
            "WHO TO CONTACT FOR HELP", "WHO TO CONTACT FOR HELP (updated)", StringComparison.Ordinal);
        await reopened.SaveEstateLetterCommand.ExecuteAsync(null);

        vm.ExitSetupCommand.Execute(null);
        vm.EnterSetupCommand.Execute(null);
        Assert.Contains("WHO TO CONTACT FOR HELP (updated)", vm.Setup!.EstateLetterText, StringComparison.Ordinal);
        vm.Lock();
    }
}
