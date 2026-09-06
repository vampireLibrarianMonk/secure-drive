using System.Text;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>Operational log persistence (spec section 20): encrypted inside the vault, metadata only.</summary>
public class OperationLogStoreTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir;

    public OperationLogStoreTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose() => Directory.Delete(vaultDir, recursive: true);

    [Fact]
    public void SaveAndLoad_RoundTripsEntries()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        var log = new OperationLog();
        log.Append("Update", "Update committed: version 2026.09.06.001.");
        log.Append("Verify", "Verification passed: 42 document(s) checked.");
        OperationLogStore.Save(session, log);

        using VaultSession session2 = VaultStore.Unlock(vaultDir, Password);
        OperationLog loaded = OperationLogStore.Load(session2);

        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal("Update", loaded.Entries[0].Category);
        Assert.Equal("Verification passed: 42 document(s) checked.", loaded.Entries[1].Message);
    }

    [Fact]
    public void Load_WithoutLogFile_ReturnsEmptyLog()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);

        OperationLog loaded = OperationLogStore.Load(session);

        Assert.Empty(loaded.Entries);
    }

    [Fact]
    public void LogFile_IsEncrypted_SpecSection20()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            var log = new OperationLog();
            log.Append("Update", "Update committed: version 2026.09.06.001.");
            OperationLogStore.Save(session, log);
            Assert.True(session.FileExists(VaultPaths.LogsPath));
        }

        // Spec section 20: NO physical file on the drive may contain log
        // plaintext — the log lives inside the encrypted vault.
        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(vaultDir, "*", SearchOption.AllDirectories))
        {
            string rawText = Encoding.Latin1.GetString(File.ReadAllBytes(file));
            if (rawText.Contains("Update committed", StringComparison.OrdinalIgnoreCase)
                || rawText.Contains("Verification passed", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(file);
            }
        }

        Assert.Empty(offenders);
    }
}
