using EmergencyArchive.Crypto.Vault;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// The scenario suites drive the full application journeys (drive setup, unlock,
/// password manager, document import/search, estate letter) and all rely on the
/// <see cref="VaultLocator.VaultPathEnvironmentVariable"/> process-wide override,
/// so they must not run concurrently with each other.
/// </summary>
[CollectionDefinition("scenario", DisableParallelization = true)]
public sealed class ScenarioCollection;

/// <summary>Shared helpers for the end-to-end scenario tests.</summary>
public static class ScenarioSupport
{
    /// <summary>A long passphrase that satisfies <see cref="EmergencyArchive.Crypto.PasswordPolicy"/>.</summary>
    public const string Password = "correct horse battery staple";

    /// <summary>
    /// Creates a temporary directory tree shaped like a freshly prepared drive
    /// (scripts/setup-drive.ps1 -SkipVault): app\windows with the launcher,
    /// an empty public\ folder, and no vault yet.
    /// </summary>
    public static string NewDriveRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emergencyarchive-drive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "app", "windows"));
        File.WriteAllText(Path.Combine(root, "app", "windows", "START-WINDOWS.exe"), "placeholder");
        Directory.CreateDirectory(Path.Combine(root, "public"));
        return root;
    }

    /// <summary>Creates a vault with test-speed scrypt parameters (the suite exercises the real cost parameters exactly once).</summary>
    public static void CreateCheapVault(string vaultDir, string password) =>
        VaultStore.Create(vaultDir, password, scryptCostParam: 1 << 10, scryptBlockSize: 1);

    /// <summary>Unique temp path for a vault directory.</summary>
    public static string NewVaultPath() =>
        Path.Combine(Path.GetTempPath(), $"emergencyarchive-vault-{Guid.NewGuid():N}");

    /// <summary>Points the app's vault locator at the given vault (or clears it with null).</summary>
    public static void SetVaultOverride(string? vaultPath) =>
        Environment.SetEnvironmentVariable(EmergencyArchive.Core.VaultLocator.VaultPathEnvironmentVariable, vaultPath);

    /// <summary>Waits until the condition holds or the timeout elapses (for background flows such as indexing or async-void imports).</summary>
    public static async Task WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }
}
