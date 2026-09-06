using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>Source management and recovery-instructions export.</summary>
public sealed partial class SetupViewModel
{
    private void ReloadSources()
    {
        Sources.Clear();
        foreach (SourceDirectory source in CurrentSources().Sources)
        {
            Sources.Add(new SourceItemViewModel(source.EffectiveAlias, source.Path));
        }

        SourceCount = Sources.Count;
    }

    /// <summary>Called by the view after a folder picker returned a path.</summary>
    public void AddSourceFromPath(string path)
    {
        if (!Directory.Exists(path))
        {
            SetupStatus = $"Source directory does not exist: {path}";
            return;
        }

        SourceConfiguration configuration = CurrentSources().WithSource(new SourceDirectory(Path.GetFullPath(path)));
        SourceConfigStore.Save(session, configuration);
        RefreshDashboard();
        RecordActivity("Sources", $"Source added: {configuration.Sources.Last().EffectiveAlias} ({Path.GetFullPath(path)}).");
        SetupStatus = $"Source added: {configuration.Sources.Last().EffectiveAlias}. Run UPDATE ARCHIVE to import its documents.";
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSource))]
    private void RemoveSource()
    {
        if (SelectedSource is null)
        {
            return;
        }

        string removedAlias = SelectedSource.Alias;
        SourceConfiguration configuration = CurrentSources().WithoutSource(removedAlias);
        SourceConfigStore.Save(session, configuration);
        SelectedSource = null;
        RefreshDashboard();
        RecordActivity("Sources", $"Source removed: {removedAlias}.");
        SetupStatus = "Source removed. Run UPDATE ARCHIVE to apply (deleted source files will be removed from the archive).";
    }

    private bool CanRemoveSource => !IsBusy && SelectedSource is not null;

    [RelayCommand(CanExecute = nameof(CanUpdateOrVerify))]
    private void ExportRecoveryInstructions()
    {
        string vaultRoot = Path.GetDirectoryName(vaultPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? throw new InvalidOperationException("Unable to determine the drive root.");
        string target = Path.Combine(vaultRoot, "public", RecoveryInstructionsFileName);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, RecoveryInstructionsText, Encoding.UTF8);
        RecordActivity("Recovery", $"Recovery instructions exported to {target}.");
        SetupStatus = $"Recovery instructions exported to {target}";
    }

    /// <summary>Keep in sync with scripts/new-usb.ps1 (same content, deployed at drive preparation).</summary>
    internal static string RecoveryInstructionsText { get; } = BuildRecoveryInstructions();
}
