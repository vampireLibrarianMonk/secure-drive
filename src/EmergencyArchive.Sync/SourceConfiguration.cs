using System.IO;
using System.Text.Json;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;

namespace EmergencyArchive.Sync;

/// <summary>Persists the source configuration as an encrypted file inside the vault.</summary>
public static class SourceConfigStore
{
    public const string ConfigPath = VaultPaths.SourcesPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static SourceConfiguration Load(VaultSession session)
    {
        if (!session.FileExists(ConfigPath))
        {
            return SourceConfiguration.Empty;
        }

        return JsonSerializer.Deserialize<SourceConfiguration>(session.ReadFile(ConfigPath), JsonOptions) ?? SourceConfiguration.Empty;
    }

    public static void Save(VaultSession session, SourceConfiguration configuration)
    {
        using var target = new MemoryStream();
        JsonSerializer.Serialize(target, configuration, JsonOptions);
        target.Position = 0;
        session.WriteFile(ConfigPath, target);
    }
}

/// <summary>One source directory the archive is built from (spec section 13).</summary>
public sealed record SourceDirectory(
    string Path,
    string? Alias = null,
    bool Recursive = true,
    IReadOnlyList<string>? ExcludePatterns = null)
{
    /// <summary>The folder name used inside the archive; defaults to the source directory's leaf name.</summary>
    public string EffectiveAlias => Alias ?? System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    /// <summary>Owner-defined exclusion patterns (wildcards * and ?) applied on top of the defaults.</summary>
    public IReadOnlyList<string> EffectiveExcludePatterns => ExcludePatterns ?? [];
}

/// <summary>The set of source directories configured for an archive.</summary>
public sealed record SourceConfiguration(IReadOnlyList<SourceDirectory> Sources)
{
    public static readonly SourceConfiguration Empty = new([]);

    public bool IsEmpty => Sources.Count == 0;

    /// <summary>Adds or replaces a source (keyed by its effective alias).</summary>
    public SourceConfiguration WithSource(SourceDirectory source)
    {
        var others = Sources.Where(s => !string.Equals(s.EffectiveAlias, source.EffectiveAlias, StringComparison.OrdinalIgnoreCase));
        return new SourceConfiguration([.. others, source]);
    }

    /// <summary>Removes the source with the given effective alias.</summary>
    public SourceConfiguration WithoutSource(string alias)
    {
        return new SourceConfiguration([.. Sources.Where(s => !string.Equals(s.EffectiveAlias, alias, StringComparison.OrdinalIgnoreCase))]);
    }
}
