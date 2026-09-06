using System.IO;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>Verify and index rebuild commands (spec sections 12, 16, 24).</summary>
public sealed partial class SetupViewModel
{
    [RelayCommand(CanExecute = nameof(CanUpdateOrVerify))]
    private async Task VerifyAsync()
    {
        IsBusy = true;
        SetupStatus = "Verifying archive… (re-hashing every document)";
        try
        {
            ArchiveManifest manifest = ManifestStore.Load(session)
                ?? ArchiveManifest.Empty(session.VaultId, "not committed");

            var files = await Task.Run(() => session.EnumerateFiles().ToList());
            int stale = getIndex()?.CountStaleEntries(manifest) ?? 0;

            ArchiveVerificationReport report = await Task.Run(() => ArchiveVerifier.Verify(
                manifest,
                files,
                relativePath => new MemoryStream(session.ReadFile(relativePath)),
                VaultPaths.InfrastructurePaths));

            IntegrityStatus = report.Healthy
                ? $"HEALTHY — {report.DocumentsChecked} document(s), {ArchiveDashboardSnapshot.FormatBytes(report.BytesChecked)} verified."
                : $"ISSUES: {report.Corrupt.Count} corrupt, {report.Missing.Count} missing, {report.Unexpected.Count} unexpected, {report.IndexStaleEntries} stale index entries. Do not modify this drive.";
            RecordActivity("Verify", report.Healthy
                ? $"Verification passed: {report.DocumentsChecked} document(s) checked."
                : $"Verification found issues: {report.Corrupt.Count} corrupt, {report.Missing.Count} missing, {report.Unexpected.Count} unexpected.");
        }
        catch (Exception e) when (e is VaultException or IOException)
        {
            IntegrityStatus = $"Verification failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRebuildIndex))]
    private async Task RebuildIndexAsync()
    {
        IsBusy = true;
        SetupStatus = "Rebuilding search index…";
        try
        {
            getIndex()?.Dispose();
            setIndex(null);

            VaultSearchIndex rebuilt = await Task.Run(() => VaultSearchIndex.Build(session));
            setIndex(rebuilt);
            RecordActivity("Index", $"Search index rebuilt ({rebuilt.DocumentCount} document(s) indexed).");
            SetupStatus = $"Search index rebuilt ({rebuilt.DocumentCount} document(s) indexed).";
        }
        catch (VaultException e)
        {
            SetupStatus = $"Index rebuild failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
