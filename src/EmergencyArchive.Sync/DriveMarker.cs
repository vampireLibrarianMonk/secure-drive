using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EmergencyArchive.Sync;

/// <summary>
/// Refreshes the drive marker file (<c>.emergency-archive-drive.json</c>,
/// written by scripts/new-usb.ps1) after an archive update, so the drive label
/// always reflects the committed archive identity and version (spec section 17).
/// </summary>
public static class DriveMarker
{
    public const string MarkerFileName = ".emergency-archive-drive.json";

    /// <summary>
    /// Updates the marker next to the vault with the replica's committed state.
    /// Returns false when the drive has no marker file (nothing to refresh).
    /// </summary>
    public static bool TryRefresh(string vaultPath, ReplicaInfo info)
    {
        string markerPath = GetMarkerPath(vaultPath);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        JsonNode? node = JsonNode.Parse(File.ReadAllText(markerPath));
        if (node is null)
        {
            return false;
        }

        node["archiveId"] = info.ArchiveId;
        node["archiveVer"] = info.ArchiveVersion;
        node["lastSyncUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        File.WriteAllText(markerPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    public static string GetMarkerPath(string vaultPath)
    {
        string vaultRoot = Path.GetFullPath(vaultPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string driveRoot = Path.GetDirectoryName(vaultRoot) ?? vaultRoot;
        return Path.Combine(driveRoot, MarkerFileName);
    }
}
