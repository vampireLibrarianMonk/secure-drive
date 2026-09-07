using System.IO;
using EmergencyArchive.Core;
using Xunit;

namespace EmergencyArchive.Core.Tests;

public class VaultLocatorTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ea-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Locate_ReturnsNull_WhenNoVaultPresent()
    {
        string baseDir = NewTempDir();
        try
        {
            Assert.Null(VaultLocator.Locate(baseDir, _ => null));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void Locate_FindsVault_ByWalkingUpFromAppDirectory()
    {
        // Simulate the on-drive layout: <root>\vault\masterkey.cryptomator with
        // the app nested at <root>\app\windows.
        string root = NewTempDir();
        try
        {
            string vault = Path.Combine(root, VaultLocator.VaultFolderName);
            Directory.CreateDirectory(vault);
            File.WriteAllText(Path.Combine(vault, VaultLocator.VaultMarkerFile), "{}");

            string appDir = Path.Combine(root, "app", "windows");
            Directory.CreateDirectory(appDir);

            string? found = VaultLocator.Locate(appDir, _ => null);

            Assert.NotNull(found);
            Assert.Equal(
                Path.GetFullPath(vault).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(found!).TrimEnd(Path.DirectorySeparatorChar));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LocateCreatable_ReturnsVaultAtVolumeRoot_ForFirstRun()
    {
        // No vault exists yet; the create target is <root>\vault regardless of
        // how deeply the app is nested.
        string root = NewTempDir();
        try
        {
            string appDir = Path.Combine(root, "app", "windows");
            Directory.CreateDirectory(appDir);

            string target = VaultLocator.LocateCreatable(appDir, _ => null);

            string expectedRoot = Path.GetPathRoot(Path.GetFullPath(appDir))!;
            Assert.Equal(
                Path.Combine(expectedRoot, VaultLocator.VaultFolderName),
                target);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnvironmentOverride_WinsForBothLocateAndLocateCreatable()
    {
        string root = NewTempDir();
        try
        {
            string vault = Path.Combine(root, VaultLocator.VaultFolderName);
            Directory.CreateDirectory(vault);
            File.WriteAllText(Path.Combine(vault, VaultLocator.VaultMarkerFile), "{}");

            string? Env(string name) =>
                name == VaultLocator.VaultPathEnvironmentVariable ? vault : null;

            Assert.Equal(vault, VaultLocator.Locate(root, Env));
            Assert.Equal(vault, VaultLocator.LocateCreatable(root, Env));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
