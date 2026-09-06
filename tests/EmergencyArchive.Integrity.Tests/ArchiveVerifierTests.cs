using System.Text;
using EmergencyArchive.Integrity;
using Xunit;

namespace EmergencyArchive.Integrity.Tests;

public class ArchiveVerifierTests
{
    private static ArchiveManifest ManifestWithEntries(params ManifestEntry[] entries) => new(
        "EmergencyArchive",
        "2026.09.06.001",
        DateTimeOffset.UtcNow,
        "0.1.0",
        entries);

    private static ManifestEntry Entry(string path, string content)
    {
        return new ManifestEntry(path, content.Length, DateTimeOffset.UtcNow, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content))));
    }

    [Fact]
    public void Verify_HealthyVault_ReportsHealthy()
    {
        var manifest = ManifestWithEntries(Entry("a.txt", "content a"), Entry("b.txt", "content b"));
        var stored = new[] { "a.txt", "b.txt", "index/search.index" };

        ArchiveVerificationReport report = ArchiveVerifier.Verify(
            manifest,
            stored,
            path => new MemoryStream(Encoding.UTF8.GetBytes(path == "a.txt" ? "content a" : "content b")),
            infrastructurePaths: ["index/search.index"]);

        Assert.True(report.Healthy);
        Assert.Equal(2, report.DocumentsChecked);
        Assert.Equal(2, report.Valid.Count);
        Assert.Empty(report.Corrupt);
        Assert.Empty(report.Missing);
        Assert.Empty(report.Unexpected);
    }

    [Fact]
    public void Verify_ModifiedContent_IsDetectedAsCorrupt()
    {
        var manifest = ManifestWithEntries(Entry("a.txt", "content a"));
        var stored = new[] { "a.txt" };

        ArchiveVerificationReport report = ArchiveVerifier.Verify(
            manifest,
            stored,
            _ => new MemoryStream(Encoding.UTF8.GetBytes("TAMPERED CONTENT")),
            infrastructurePaths: []);

        Assert.False(report.Healthy);
        Assert.Equal("a.txt", Assert.Single(report.Corrupt));
    }

    [Fact]
    public void Verify_MissingFile_IsDetected()
    {
        var manifest = ManifestWithEntries(Entry("a.txt", "content a"), Entry("gone.txt", "content gone"));
        var stored = new[] { "a.txt" };

        ArchiveVerificationReport report = ArchiveVerifier.Verify(
            manifest,
            stored,
            _ => new MemoryStream(Encoding.UTF8.GetBytes("content a")),
            infrastructurePaths: []);

        Assert.False(report.Healthy);
        Assert.Equal("gone.txt", Assert.Single(report.Missing));
    }

    [Fact]
    public void Verify_UnexpectedFile_IsReported()
    {
        var manifest = ManifestWithEntries(Entry("a.txt", "content a"));
        var stored = new[] { "a.txt", "mystery.txt" };

        ArchiveVerificationReport report = ArchiveVerifier.Verify(
            manifest,
            stored,
            _ => new MemoryStream(Encoding.UTF8.GetBytes("content a")),
            infrastructurePaths: []);

        Assert.Equal("mystery.txt", Assert.Single(report.Unexpected));
    }

    [Fact]
    public void Verify_StaleIndexEntries_BreakHealth()
    {
        var manifest = ManifestWithEntries(Entry("a.txt", "content a"));

        ArchiveVerificationReport report = ArchiveVerifier.Verify(
            manifest,
            new[] { "a.txt" },
            _ => new MemoryStream(Encoding.UTF8.GetBytes("content a")),
            infrastructurePaths: [],
            indexStaleEntries: 1);

        Assert.False(report.Healthy);
    }
}
