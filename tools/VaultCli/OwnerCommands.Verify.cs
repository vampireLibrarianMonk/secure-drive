using System.IO;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace VaultCli;

/// <summary>Archive verification command (spec section 16).</summary>
internal static partial class OwnerCommands
{
    public static int Verify(string[] args, Func<string, string> prompt)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: VaultCli verify <vault-directory>");
            return 1;
        }

        using VaultSession session = VaultStore.Unlock(args[1], prompt("Archive password: "));
        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty("EmergencyArchive", ArchiveUpdater.NextVersion(null, DateTimeOffset.UtcNow));

        string[] infrastructurePaths = [.. VaultPaths.InfrastructurePaths];

        int staleEntries = 0;
        if (session.FileExists(VaultSearchIndex.IndexPath))
        {
            using VaultSearchIndex index = VaultSearchIndex.LoadOrBuild(session);
            staleEntries = index.CountStaleEntries(manifest);
        }

        ArchiveVerificationReport report = ArchiveVerifier.Verify(
            manifest,
            session.EnumerateFiles(),
            relativePath => new MemoryStream(session.ReadFile(relativePath)),
            infrastructurePaths,
            staleEntries);

        Console.WriteLine("ARCHIVE VERIFICATION");
        Console.WriteLine($"  Documents checked: {report.DocumentsChecked}");
        Console.WriteLine($"  Data checked:      {report.BytesChecked} bytes");
        Console.WriteLine($"  Valid:             {report.Valid.Count}");
        Console.WriteLine($"  Corrupt:           {report.Corrupt.Count}");
        Console.WriteLine($"  Missing:           {report.Missing.Count}");
        Console.WriteLine($"  Unexpected:        {report.Unexpected.Count}");
        Console.WriteLine($"  Stale index:       {report.IndexStaleEntries}");

        foreach (string path in report.Corrupt.Take(10))
        {
            Console.WriteLine($"  CORRUPT: {path}");
        }

        foreach (string path in report.Missing.Take(10))
        {
            Console.WriteLine($"  MISSING: {path}");
        }

        Console.WriteLine(report.Healthy ? "OVERALL: HEALTHY" : "OVERALL: ISSUES FOUND — do not modify this drive.");

        return report.Healthy ? 0 : 2;
    }
}
