using EmergencyArchive.Core;
using Xunit;

namespace EmergencyArchive.Core.Tests;

public class VaultPathsTests
{
    [Theory]
    [InlineData("index/search.index")]
    [InlineData("manifest/archive-manifest.json")]
    [InlineData("sources/sources.json")]
    [InlineData("logs/operations.log")]
    public void IsInfrastructurePath_RecognizesInfrastructureFiles(string path)
    {
        // These are the vault's own files. The document browse list and the
        // search index both rely on this to keep them out of user-facing
        // document results.
        Assert.True(VaultPaths.IsInfrastructurePath(path));
    }

    [Theory]
    [InlineData("Insurance/Home policy.pdf")]
    [InlineData("Estate Plan/READ-ME-FIRST — Letter to my family.txt")]
    [InlineData("passport.pdf")]
    [InlineData("Legal/will.docx")]
    public void IsInfrastructurePath_LeavesRealDocumentsAlone(string path)
    {
        Assert.False(VaultPaths.IsInfrastructurePath(path));
    }

    [Fact]
    public void InfrastructurePaths_ContainsAllFourKnownFiles()
    {
        Assert.Equal(4, VaultPaths.InfrastructurePaths.Count);
        Assert.Contains(VaultPaths.IndexPath, VaultPaths.InfrastructurePaths);
        Assert.Contains(VaultPaths.ManifestPath, VaultPaths.InfrastructurePaths);
        Assert.Contains(VaultPaths.SourcesPath, VaultPaths.InfrastructurePaths);
        Assert.Contains(VaultPaths.LogsPath, VaultPaths.InfrastructurePaths);
    }
}
