using System.IO;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace VaultCli;

/// <summary>Replica inspection and comparison commands (spec section 17).</summary>
internal static partial class OwnerCommands
{
    public static int Replica(string[] args, Func<string, string> prompt)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: VaultCli replica <vault-directory>");
            return 1;
        }

        using VaultSession session = VaultStore.Unlock(args[1], prompt("Archive password: "));
        ReplicaInfo info = ReplicaInspector.Inspect(session, args[1]);
        PrintReplica(info);
        return 0;
    }

    public static int Replicas(string[] args, Func<string, string> prompt)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: VaultCli replicas <vault1> <vault2> [vault3 …]  (one password prompt, used for all)");
            return 1;
        }

        string password = prompt("Archive password (used for all listed drives): ");
        List<ReplicaInfo> infos = [];
        foreach (string vault in args.Skip(1))
        {
            try
            {
                using VaultSession session = VaultStore.Unlock(vault, password);
                infos.Add(ReplicaInspector.Inspect(session, vault));
            }
            catch (VaultUnlockException)
            {
                Console.WriteLine($"  {vault}: cannot unlock with this password — skipped.");
            }
        }

        if (infos.Count == 0)
        {
            Console.Error.WriteLine("No replica could be inspected.");
            return 2;
        }

        foreach (ReplicaInfo info in infos)
        {
            PrintReplica(info);
        }

        // Pairwise verdicts against the newest committed replica.
        var committed = infos.Where(i => i.IsCommitted).ToList();
        if (committed.Count == 0)
        {
            Console.WriteLine("No replica has a committed archive revision yet.");
            return 0;
        }

        string archiveId = committed[0].ArchiveId;
        long newestRevision = committed.Max(i => i.Revision);
        Console.WriteLine();
        Console.WriteLine($"Archive '{archiveId}' — newest committed revision: {newestRevision}");

        foreach (ReplicaInfo info in infos)
        {
            if (!info.IsCommitted)
            {
                Console.WriteLine($"  {info.VaultPath}: NEVER COMMITTED");
                continue;
            }

            if (info.Revision == newestRevision)
            {
                Console.WriteLine($"  {info.VaultPath}: UP TO DATE (revision {info.Revision})");
            }
            else
            {
                Console.WriteLine($"  {info.VaultPath}: OUTDATED (revision {info.Revision} < {newestRevision}) — run UPDATE or copy from the newest drive");
            }
        }

        // Divergence check among replicas at the newest revision.
        var atNewest = committed.Where(i => i.Revision == newestRevision).ToList();
        bool diverged = atNewest.Select(i => i.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        if (diverged)
        {
            Console.WriteLine("WARNING: replicas at the newest revision have DIFFERENT content — they diverged. Reconcile by re-running the update from the master sources on each drive.");
        }

        return 0;
    }

    private static void PrintReplica(ReplicaInfo info)
    {
        Console.WriteLine($"  {info.VaultPath}");
        Console.WriteLine($"    archive:   {info.ArchiveId} @ {info.ArchiveVersion} (revision {info.Revision})");
        Console.WriteLine($"    documents: {info.DocumentCount} ({ArchiveDashboardSnapshot.FormatBytes(info.TotalLogicalBytes)}), last update {info.LastUpdateUtc:yyyy-MM-dd HH:mm} UTC");
    }
}
