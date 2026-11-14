using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>
/// Red/blue cycle (2026-11): the source scanner must never follow symlinks or
/// junctions planted inside a source folder (they can pull files from outside
/// the source into the archive, or create traversal cycles), and single files
/// above the per-document size cap are excluded from scanning and import.
/// </summary>
public sealed class SourceScannerHardeningTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string tempRoot;
    private readonly string sourceDir;
    private readonly string outsideDir;

    public SourceScannerHardeningTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), $"ea-scanner-{Guid.NewGuid():N}");
        sourceDir = Path.Combine(tempRoot, "source");
        outsideDir = Path.Combine(tempRoot, "outside");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(sourceDir, "keep.txt"), "legitimate document");
        File.WriteAllText(Path.Combine(sourceDir, "sub", "nested.txt"), "legitimate nested document");
        File.WriteAllText(Path.Combine(outsideDir, "secret-outside.txt"), "must never be scanned");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private ScannedFileSet Scan() => SourceScanner.Scan(new SourceConfiguration(
        [new SourceDirectory(Path.Combine(tempRoot, "source"))]));

    private bool TryCreateFileSymlink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return false; // Windows without developer mode / unprivileged shell
        }
    }

    [Fact]
    public void Scan_ReturnsRegularFiles_WithArchivePaths()
    {
        ScannedFileSet result = Scan();

        Assert.Contains(result.Files, f => f.RelativePath == "source/keep.txt");
        Assert.Contains(result.Files, f => f.RelativePath == "source/sub/nested.txt");
        Assert.DoesNotContain(result.Files, f => f.RelativePath.Contains("secret-outside", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_SkipsPlantedFileSymlinks()
    {
        if (!TryCreateFileSymlink(Path.Combine(sourceDir, "leak.txt"), Path.Combine(outsideDir, "secret-outside.txt")))
        {
            return; // environment cannot create symlinks; CI (Linux container) exercises this
        }

        ScannedFileSet result = Scan();

        Assert.Contains(result.Files, f => f.RelativePath == "source/keep.txt");
        Assert.DoesNotContain(result.Files, f => f.RelativePath == "source/leak.txt");
        Assert.DoesNotContain(result.Files, f => f.RelativePath.Contains("secret-outside", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_DoesNotTraversePlantedDirectorySymlinks()
    {
        if (!TryCreateFileSymlink(Path.Combine(sourceDir, "linked-dir"), outsideDir))
        {
            return; // environment cannot create symlinks; CI (Linux container) exercises this
        }

        ScannedFileSet result = Scan();

        Assert.DoesNotContain(result.Files, f => f.RelativePath.Contains("linked-dir", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Files, f => f.RelativePath.Contains("secret-outside", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_SkipsFilesAboveTheSizeCap_WithoutHashingThem()
    {
        // Sparse 3 GiB file (instant to create; the cap must exclude it before
        // any hashing, otherwise this scan would take minutes).
        string hugePath = Path.Combine(sourceDir, "huge.bin");
        using (FileStream stream = File.Create(hugePath))
        {
            stream.SetLength(SourceScanner.MaxScanFileBytes + 1);
        }

        ScannedFileSet result = Scan();

        Assert.Contains(result.Files, f => f.RelativePath == "source/keep.txt");
        Assert.DoesNotContain(result.Files, f => f.RelativePath == "source/huge.bin");
    }
}
