using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

public class SourceScannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"emergencyarchive-scan-{Guid.NewGuid():N}");

    public SourceScannerTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(root, "junk.tmp"), "junk");
        File.WriteAllText(Path.Combine(root, "~$lockfile.docx"), "lock");
        File.WriteAllText(Path.Combine(root, "Thumbs.db"), "thumbs");
        Directory.CreateDirectory(Path.Combine(root, "Nested"));
        File.WriteAllText(Path.Combine(root, "Nested", "nested.txt"), "nested");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Scan_MapsFilesUnderTheAlias_AndAppliesDefaults()
    {
        var configuration = new SourceConfiguration([new SourceDirectory(root)]);

        ScannedFileSet scan = SourceScanner.Scan(configuration);

        string[] paths = [.. scan.Files.Select(f => f.RelativePath)];
        Assert.Equal([$"{Path.GetFileName(root)}/Nested/nested.txt", $"{Path.GetFileName(root)}/keep.txt"], paths);
    }

    [Fact]
    public void Scan_NonRecursive_IgnoresSubfolders()
    {
        var configuration = new SourceConfiguration([new SourceDirectory(root, Recursive: false)]);

        ScannedFileSet scan = SourceScanner.Scan(configuration);

        Assert.Single(scan.Files);
    }

    [Fact]
    public void Scan_ComputesSizeModificationTimeAndHash()
    {
        var configuration = new SourceConfiguration([new SourceDirectory(root)]);

        ScannedFileSet scan = SourceScanner.Scan(configuration);

        ScannedFile keep = scan.Files.Single(f => f.RelativePath.EndsWith("keep.txt"));
        Assert.Equal(new FileInfo(Path.Combine(root, "keep.txt")).Length, keep.Size);
        Assert.Equal(64, keep.Sha256.Length);
        Assert.True(keep.ModifiedTimeUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Scan_WithCustomExcludePattern_AppliesIt()
    {
        var configuration = new SourceConfiguration(
        [
            new SourceDirectory(root, ExcludePatterns: ["keep.txt"]),
        ]);

        ScannedFileSet scan = SourceScanner.Scan(configuration);

        Assert.DoesNotContain(scan.Files, f => f.RelativePath.EndsWith("keep.txt"));
    }

    [Fact]
    public void Scan_WithMissingSourceDirectory_Throws()
    {
        var configuration = new SourceConfiguration([new SourceDirectory(Path.Combine(root, "does-not-exist"))]);

        Assert.Throws<DirectoryNotFoundException>(() => SourceScanner.Scan(configuration));
    }
}
