namespace EmergencyArchive.Core;

/// <summary>
/// Vault-relative paths of infrastructure files (not documents): the search
/// index, the integrity manifest, and the source configuration. They live in
/// the vault like documents but are never indexed, verified as documents, or
/// listed in search results.
/// </summary>
public static class VaultPaths
{
    public const string IndexPath = "index/search.index";
    public const string ManifestPath = "manifest/archive-manifest.json";
    public const string SourcesPath = "sources/sources.json";

    public static readonly IReadOnlyList<string> InfrastructurePaths = [IndexPath, ManifestPath, SourcesPath];

    public static bool IsInfrastructurePath(string relativePath)
    {
        return InfrastructurePaths.Contains(relativePath, StringComparer.OrdinalIgnoreCase);
    }
}
