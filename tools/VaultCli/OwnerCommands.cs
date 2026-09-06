using System.IO;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace VaultCli;

/// <summary>Phase 3 owner commands: source configuration and updates.</summary>
internal static partial class OwnerCommands
{
    public static int Sources(string[] args, Func<string, string> prompt)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: VaultCli sources <vault-directory>");
            return 1;
        }

        using VaultSession session = VaultStore.Unlock(args[1], prompt("Archive password: "));
        SourceConfiguration configuration = SourceConfigStore.Load(session);
        if (configuration.IsEmpty)
        {
            Console.WriteLine("No sources configured. Add one with: VaultCli sources-add <vault> <source-directory> [alias]");
            return 0;
        }

        foreach (SourceDirectory source in configuration.Sources)
        {
            Console.WriteLine($"  {source.EffectiveAlias,-20} {source.Path}");
        }

        return 0;
    }

    public static int SourcesAdd(string[] args, Func<string, string> prompt)
    {
        if (args.Length is not (3 or 4))
        {
            Console.Error.WriteLine("Usage: VaultCli sources-add <vault-directory> <source-directory> [alias]");
            return 1;
        }

        string sourceDirectory = Path.GetFullPath(args[2]);
        if (!Directory.Exists(sourceDirectory))
        {
            Console.Error.WriteLine($"Source directory does not exist: {sourceDirectory}");
            return 1;
        }

        using VaultSession session = VaultStore.Unlock(args[1], prompt("Archive password: "));
        SourceConfiguration configuration = SourceConfigStore.Load(session).WithSource(
            new SourceDirectory(sourceDirectory, args.Length == 4 ? args[3] : null));
        SourceConfigStore.Save(session, configuration);

        Console.WriteLine("Sources configured:");
        foreach (SourceDirectory source in configuration.Sources)
        {
            Console.WriteLine($"  {source.EffectiveAlias,-20} {source.Path}");
        }

        return 0;
    }

    public static int Update(string[] args, Func<string, string> prompt)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: VaultCli update <vault-directory>");
            return 1;
        }

        using VaultSession session = VaultStore.Unlock(args[1], prompt("Archive password: "));
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty("EmergencyArchive", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));
        SourceConfiguration sources = SourceConfigStore.Load(session);
        if (sources.IsEmpty)
        {
            Console.Error.WriteLine("No sources configured. Add one with: VaultCli sources-add <vault> <source-directory> [alias]");
            return 1;
        }

        ArchiveUpdateReport report = ArchiveUpdater.Update(
            session,
            manifest,
            sources,
            new Progress<ArchiveUpdateProgress>(p =>
                Console.WriteLine($"  [{p.Phase}] {p.Processed}/{p.Total} {p.CurrentItem}")));

        if (report.NoChanges)
        {
            Console.WriteLine($"Archive is up to date (version {report.ArchiveVersion}, {report.DocumentCount} documents).");
            return 0;
        }

        Console.WriteLine($"Added {report.Plan.Added.Count}, changed {report.Plan.Changed.Count}, removed {report.Plan.Deleted.Count}.");
        Console.WriteLine($"Archive version {report.ArchiveVersion} committed: {report.DocumentCount} documents, {report.TotalLogicalBytes} bytes.");

        // Refresh the drive marker so the drive label reflects the new state
        // (spec section 17: replica identification).
        ReplicaInfo replicaInfo = ReplicaInspector.Inspect(session, args[1]);
        if (DriveMarker.TryRefresh(args[1], replicaInfo))
        {
            Console.WriteLine("Drive marker refreshed.");
        }

        // Bring the search index along incrementally (added/changed/deleted).
        using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
        index.ApplyChanges(session, report.Plan, report.Manifest);
        index.Save(session);
        Console.WriteLine($"Search index updated ({index.DocumentCount} documents indexed, {index.CountStaleEntries(report.Manifest)} stale).");

        return 0;
    }
}
