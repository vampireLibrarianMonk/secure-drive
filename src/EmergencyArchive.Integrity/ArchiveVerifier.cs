namespace EmergencyArchive.Integrity;

/// <summary>The result of a full archive verification (spec section 16).</summary>
public sealed record ArchiveVerificationReport(
    int DocumentsChecked,
    long BytesChecked,
    IReadOnlyList<string> Valid,
    IReadOnlyList<string> Corrupt,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unexpected,
    int IndexStaleEntries)
{
    /// <summary>Healthy only when every manifest entry verified and the index agrees (spec section 16).</summary>
    public bool Healthy => Corrupt.Count == 0 && Missing.Count == 0 && IndexStaleEntries == 0 && Unexpected.Count == 0;
}

/// <summary>
/// Verifies an archive against its manifest (spec section 16): every stored
/// document is re-hashed and compared, missing documents are detected, and
/// files that are not in the manifest are reported. The caller supplies the
/// stored file list and a content opener so this stays independent of the
/// vault storage layer. Index consistency is reported via
/// <paramref name="indexStaleEntries"/>.
/// </summary>
public static class ArchiveVerifier
{
    public static ArchiveVerificationReport Verify(
        ArchiveManifest manifest,
        IEnumerable<string> storedFilePaths,
        Func<string, Stream> openContent,
        IEnumerable<string> infrastructurePaths,
        int indexStaleEntries = 0,
        int maxReportedIssues = 25)
    {
        var stored = new HashSet<string>(storedFilePaths, StringComparer.OrdinalIgnoreCase);
        var infrastructure = new HashSet<string>(infrastructurePaths, StringComparer.OrdinalIgnoreCase);

        List<string> valid = [];
        List<string> corrupt = [];
        List<string> missing = [];
        List<string> unexpected = [];
        long bytesChecked = 0;
        int documentsChecked = 0;

        foreach (ManifestEntry entry in manifest.Entries)
        {
            if (!stored.Contains(entry.RelativePath))
            {
                missing.Add(entry.RelativePath);
                continue;
            }

            using Stream stream = openContent(entry.RelativePath);
            string hash = Sha256.ComputeHash(stream);
            bytesChecked += stream.Length;

            if (string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                valid.Add(entry.RelativePath);
            }
            else if (corrupt.Count < maxReportedIssues)
            {
                corrupt.Add(entry.RelativePath);
            }

            documentsChecked++;
        }

        foreach (string path in stored)
        {
            bool inManifest = manifest.Entries.Any(e => string.Equals(e.RelativePath, path, StringComparison.OrdinalIgnoreCase));
            if (!inManifest && !infrastructure.Contains(path))
            {
                unexpected.Add(path);
            }
        }

        return new ArchiveVerificationReport(documentsChecked, bytesChecked, valid, corrupt, missing, unexpected, indexStaleEntries);
    }
}
