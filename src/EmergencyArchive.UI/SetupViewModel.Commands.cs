using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>Dashboard refresh and the archive update command (spec sections 12-14).</summary>
public sealed partial class SetupViewModel
{
    private SourceConfiguration CurrentSources() => SourceConfigStore.Load(session);

    private void RefreshDashboard()
    {
        ArchiveManifest? manifest = ManifestStore.Load(session);
        SourceConfiguration sources = CurrentSources();
        ArchiveDashboardSnapshot snapshot = ArchiveDashboard.Build(vaultPath, manifest, sources);

        ArchiveId = snapshot.ArchiveId;
        ArchiveVersion = snapshot.ArchiveVersion;
        RevisionDisplay = $"#{snapshot.Revision}";
        DocumentCountDisplay = $"{snapshot.DocumentCount}";
        ArchiveSizeDisplay = snapshot.ArchiveSizeDisplay;
        DriveFreeDisplay = snapshot.DriveFreeDisplay;
        LastUpdateDisplay = snapshot.LastUpdateDisplay;
        SourceCount = snapshot.SourceCount;
    }

    [RelayCommand(CanExecute = nameof(CanUpdateOrVerify))]
    private async Task UpdateAsync()
    {
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty(session.VaultId, ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));
        SourceConfiguration sources = CurrentSources();

        IsBusy = true;
        SetupStatus = "Scanning sources…";
        try
        {
            ArchiveUpdateReport report = await Task.Run(() => ArchiveUpdater.Update(
                session,
                manifest,
                sources,
                new Progress<ArchiveUpdateProgress>(p =>
                    SetupStatus = $"[{p.Phase}] {p.Processed}/{p.Total}: {p.CurrentItem}")));

            // Bring the search index along incrementally (added/changed/deleted).
            VaultSearchIndex? index = getIndex();
            if (index is null)
            {
                index = VaultSearchIndex.LoadOrBuild(session);
                setIndex(index);
            }

            await Task.Run(() =>
            {
                index.ApplyChanges(session, report.Plan, report.Manifest);
                index.Save(session);
            });

            RefreshDashboard();
            OnDocumentsChanged();
            RecordActivity("Update", report.NoChanges
                ? $"No changes (version {report.ArchiveVersion})."
                : $"Update committed: version {report.ArchiveVersion} — added {report.Plan.Added.Count}, changed {report.Plan.Changed.Count}, removed {report.Plan.Deleted.Count}.");
            SetupStatus = report.NoChanges
                ? $"Archive is up to date (version {report.ArchiveVersion})."
                : $"Update committed: version {report.ArchiveVersion} — added {report.Plan.Added.Count}, changed {report.Plan.Changed.Count}, removed {report.Plan.Deleted.Count}. Run VERIFY ARCHIVE for a full check.";
        }
        catch (Exception e) when (e is VaultException or DirectoryNotFoundException or InvalidOperationException)
        {
            AppLog.Handled("UpdateArchive", e);
            SetupStatus = $"Update failed: {e.Message}";
            RecordActivity("Update", $"Update failed: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
