using System.IO;
using System.Text.RegularExpressions;
using EmergencyArchive.Integrity;

namespace EmergencyArchive.Sync;

/// <summary>One file found in a source scan, mapped to its archive-relative path.</summary>
public sealed record ScannedFile(
    string RelativePath,
    string SourceFilePath,
    long Size,
    DateTimeOffset ModifiedTimeUtc,
    string Sha256);

public sealed record ScannedFileSet(IReadOnlyList<ScannedFile> Files)
{
    public static readonly ScannedFileSet Empty = new([]);
}

/// <summary>
/// Scans the configured source directories (spec section 13): applies
/// exclusion patterns, maps files into archive-relative paths
/// (<c>alias/relative/path</c>), and computes size, modification time, and
/// SHA-256 for change detection.
/// </summary>
public static class SourceScanner
{
    /// <summary>Default exclusions per spec section 13, always applied.</summary>
    public static readonly IReadOnlyList<string> DefaultExcludePatterns =
    [
        "*.tmp",
        "~$*",
        "Thumbs.db",
        ".DS_Store",
        "desktop.ini",
    ];

    public static ScannedFileSet Scan(SourceConfiguration configuration)
    {
        List<ScannedFile> files = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SourceDirectory source in configuration.Sources)
        {
            if (!Directory.Exists(source.Path))
            {
                throw new DirectoryNotFoundException($"Source directory does not exist: {source.Path}");
            }

            string alias = source.EffectiveAlias;
            ValidateAlias(alias);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = source.Recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };

            foreach (string filePath in Directory.EnumerateFiles(source.Path, "*", options))
            {
                string relative = Path.GetRelativePath(source.Path, filePath).Replace('\\', '/');
                string name = Path.GetFileName(filePath);
                if (IsExcluded(name, relative, source.EffectiveExcludePatterns))
                {
                    continue;
                }

                string archivePath = $"{alias}/{relative}";
                if (!seen.Add(archivePath))
                {
                    throw new InvalidOperationException($"Two configured sources map to the same archive path: {archivePath}. Give the sources distinct aliases.");
                }

                var info = new FileInfo(filePath);
                files.Add(new ScannedFile(archivePath, filePath, info.Length, info.LastWriteTimeUtc, Sha256.ComputeFile(filePath)));
            }
        }

        files.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return new ScannedFileSet(files);
    }

    internal static bool IsExcluded(string fileName, string relativePath, IReadOnlyList<string>? customPatterns)
    {
        foreach (string pattern in DefaultExcludePatterns)
        {
            if (Wildcard.IsMatch(fileName, pattern))
            {
                return true;
            }
        }

        foreach (string pattern in customPatterns ?? [])
        {
            if (Wildcard.IsMatch(fileName, pattern) || Wildcard.IsMatch(relativePath, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias)
            || alias is "." or ".."
            || alias.Contains('/', StringComparison.Ordinal)
            || alias.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid source alias '{alias}'.");
        }
    }
}

/// <summary>Minimal wildcard matching (* and ?, case-insensitive) for exclusion patterns.</summary>
internal static class Wildcard
{
    public static bool IsMatch(string text, string pattern)
    {
        string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase);
    }
}
