using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Scenario: loading and managing documents. The owner adds a source folder,
/// runs UPDATE ARCHIVE (version committed, files under the source alias),
/// single-file adds land in the chosen category folder, full-text search finds
/// words inside the documents, export round-trips the original bytes, VERIFY
/// reports HEALTHY, and deleting a document uses the two-step confirmation.
/// </summary>
[Collection("scenario")]
public sealed class ScenarioDocumentsTests : IDisposable
{
    private readonly string vaultDir = ScenarioSupport.NewVaultPath();
    private readonly string sourceDir;
    private readonly string policyFile;
    private readonly string passportFile;
    private readonly string willFile;

    public ScenarioDocumentsTests()
    {
        ScenarioSupport.SetVaultOverride(vaultDir);
        ScenarioSupport.CreateCheapVault(vaultDir, ScenarioSupport.Password);

        sourceDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-src-{Guid.NewGuid():N}");
        string insurance = Path.Combine(sourceDir, "Insurance");
        string identity = Path.Combine(sourceDir, "Identity");
        Directory.CreateDirectory(insurance);
        Directory.CreateDirectory(identity);

        policyFile = Path.Combine(insurance, "home-policy.txt");
        File.WriteAllText(policyFile, "Home insurance policy number PXA-2026. The platypus umbrella rider covers floods.");
        passportFile = Path.Combine(identity, "passport-scan.txt");
        File.WriteAllText(passportFile, "Passport P-1234567, expires 2031.");

        willFile = Path.Combine(Path.GetTempPath(), $"emergencyarchive-will-{Guid.NewGuid():N}.txt");
        File.WriteAllText(willFile, "Last will. Notarized by the turquoise-notary office.");
    }

    public void Dispose()
    {
        ScenarioSupport.SetVaultOverride(null);
        try
        {
            Directory.Delete(vaultDir, recursive: true);
            Directory.Delete(sourceDir, recursive: true);
            File.Delete(willFile);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task SourceFolderImport_CommitsVersion_IsSearchable_AndExportsTheOriginalBytes()
    {
        var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await vm.UnlockCommand.ExecuteAsync(null);
        await ScenarioSupport.WaitAsync(() => !vm.IsIndexing, TimeSpan.FromSeconds(30));

        vm.EnterSetupCommand.Execute(null);
        var setup = vm.Setup!;
        string alias = Path.GetFileName(sourceDir);
        setup.AddSourceFromPath(sourceDir);
        Assert.Single(setup.Sources);
        Assert.Equal(alias, setup.Sources[0].Alias);

        await setup.UpdateCommand.ExecuteAsync(null);
        Assert.Contains("Update committed", setup.SetupStatus, StringComparison.Ordinal);
        Assert.NotEqual("not committed", setup.ArchiveVersion);
        Assert.Contains(setup.ManageableDocuments, p => p.EndsWith("Insurance/home-policy.txt", StringComparison.Ordinal));
        Assert.Contains(setup.ManageableDocuments, p => p.EndsWith("Identity/passport-scan.txt", StringComparison.Ordinal));

        // Browse screen lists the imported documents by name.
        vm.ExitSetupCommand.Execute(null);
        Assert.Equal(2, vm.Documents.Count);

        // Full-text search finds words INSIDE the documents, with a snippet.
        vm.SearchText = "platypus";
        vm.SearchCommand.Execute(null);
        DocumentItemViewModel? match = vm.FilteredDocuments.FirstOrDefault(d => d.Name == "home-policy.txt");
        Assert.NotNull(match);
        Assert.True(match!.HasSnippet);
        Assert.Contains("platypus", match.Snippet, StringComparison.OrdinalIgnoreCase);

        // Export round-trips the original bytes.
        string exported = Path.Combine(Path.GetTempPath(), $"emergencyarchive-export-{Guid.NewGuid():N}.txt");
        try
        {
            vm.ExportDocumentTo(match.RelativePath, exported);
            Assert.Equal(await File.ReadAllTextAsync(policyFile), await File.ReadAllTextAsync(exported));
        }
        finally
        {
            File.Delete(exported);
        }

        // VERIFY ARCHIVE re-hashes everything and reports HEALTHY.
        vm.EnterSetupCommand.Execute(null);
        await vm.Setup!.VerifyCommand.ExecuteAsync(null);
        Assert.StartsWith("HEALTHY", vm.Setup.IntegrityStatus, StringComparison.Ordinal);
        vm.Lock();
    }

    [Fact]
    public async Task SingleFileAdd_LandsInTheChosenCategory_AndIsSearchable()
    {
        var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await vm.UnlockCommand.ExecuteAsync(null);
        await ScenarioSupport.WaitAsync(() => !vm.IsIndexing, TimeSpan.FromSeconds(30));

        vm.EnterSetupCommand.Execute(null);
        vm.Setup!.SelectedImportCategory = "Legal";
        vm.Setup.AddFilesFromPaths([willFile]);
        await ScenarioSupport.WaitAsync(
            () => vm.Setup.ManageableDocuments.Contains("Legal/" + Path.GetFileName(willFile)),
            TimeSpan.FromSeconds(20));

        vm.ExitSetupCommand.Execute(null);
        Assert.Contains(vm.Documents, d => d.RelativePath == "Legal/" + Path.GetFileName(willFile));

        vm.SearchText = "turquoise-notary";
        vm.SearchCommand.Execute(null);
        Assert.Contains(vm.FilteredDocuments, d => d.Name == Path.GetFileName(willFile));
        vm.Lock();
    }

    [Fact]
    public async Task DeleteDocument_TwoStepConfirmation_RemovesItFromBrowseAndSetup()
    {
        var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
        await vm.UnlockCommand.ExecuteAsync(null);

        vm.EnterSetupCommand.Execute(null);
        var setup = vm.Setup!;
        setup.AddSourceFromPath(sourceDir);
        await setup.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(2, setup.ManageableDocuments.Count);

        // Select the policy document: the first click only arms the deletion.
        string policyPath = setup.ManageableDocuments.Single(p => p.EndsWith("Insurance/home-policy.txt", StringComparison.Ordinal));
        setup.SelectedDocumentPath = policyPath;
        setup.DeleteDocumentCommand.Execute(null);
        Assert.True(setup.DeleteArmed);
        Assert.Contains("Click DELETE again", setup.SetupStatus, StringComparison.Ordinal);
        Assert.Contains(setup.ManageableDocuments, p => p.EndsWith("home-policy.txt", StringComparison.Ordinal));

        // The second click performs it.
        await setup.DeleteDocumentCommand.ExecuteAsync(null);
        Assert.DoesNotContain(setup.ManageableDocuments, p => p.EndsWith("home-policy.txt", StringComparison.Ordinal));
        Assert.Single(setup.ManageableDocuments);

        // The browse screen no longer lists it either.
        vm.ExitSetupCommand.Execute(null);
        Assert.DoesNotContain(vm.Documents, d => d.Name == "home-policy.txt");
        Assert.Single(vm.Documents);
        vm.Lock();
    }
}
