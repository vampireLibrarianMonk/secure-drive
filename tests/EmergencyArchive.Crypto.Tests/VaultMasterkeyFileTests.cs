using EmergencyArchive.Crypto.Vault;
using System.Text.Json;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

public class VaultMasterkeyFileTests
{
    private const string Password = "river-copper-morning-lantern";

    // Fast KDF parameters for tests only; production defaults are 2^20 / 8.
    private const int TestCostParam = 1 << 10;
    private const int TestBlockSize = 1;

    private (string Dir, VaultKeys Keys) CreateKeys(string dir)
    {
        var keys = new VaultKeys(
            [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20],
            [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40]);
        VaultMasterkeyFile.Persist(dir, keys, Password, TestCostParam, TestBlockSize);
        return (dir, keys);
    }

    [Fact]
    public void PersistAndUnlock_RoundTrips()
    {
        string dir = NewTempDir();
        try
        {
            using var original = new VaultKeys(
                [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20],
                [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40]);
            VaultMasterkeyFile.Persist(dir, original, Password, TestCostParam, TestBlockSize);

            using VaultKeys unlocked = VaultMasterkeyFile.Unlock(dir, Password);

            Assert.Equal(original.EncryptionKey.ToArray(), unlocked.EncryptionKey.ToArray());
            Assert.Equal(original.MacKey.ToArray(), unlocked.MacKey.ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unlock_WithWrongPassword_ThrowsVaultUnlockException()
    {
        string dir = NewTempDir();
        try
        {
            CreateKeys(dir);

            Assert.Throws<VaultUnlockException>(() => VaultMasterkeyFile.Unlock(dir, "wrong password"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unlock_WithTamperedVersionMac_Throws()
    {
        string dir = NewTempDir();
        try
        {
            using (var keys = new VaultKeys(new byte[32], new byte[32]))
            {
                VaultMasterkeyFile.Persist(dir, keys, Password, TestCostParam, TestBlockSize);
            }

            string path = Path.Combine(dir, VaultMasterkeyFile.FileName);
            var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
            string tamperedVersionMac = Convert.ToBase64String(new byte[32]);
            string newJson = $$"""
                {"version":999,"scryptSalt":"{{json.GetProperty("scryptSalt").GetString()}}","scryptCostParam":{{json.GetProperty("scryptCostParam").GetInt32()}},"scryptBlockSize":{{json.GetProperty("scryptBlockSize").GetInt32()}},"primaryMasterKey":"{{json.GetProperty("primaryMasterKey").GetString()}}","hmacMasterKey":"{{json.GetProperty("hmacMasterKey").GetString()}}","versionMac":"{{tamperedVersionMac}}"}
                """;
            File.WriteAllText(path, newJson);

            Assert.Throws<VaultFormatException>(() => VaultMasterkeyFile.Unlock(dir, Password));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unlock_WithMissingFile_Throws()
    {
        string dir = NewTempDir();
        try
        {
            Assert.Throws<VaultFormatException>(() => VaultMasterkeyFile.Unlock(dir, Password));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Persist_WritesCryptomatorFieldNames()
    {
        string dir = NewTempDir();
        try
        {
            CreateKeys(dir);

            string json = File.ReadAllText(Path.Combine(dir, VaultMasterkeyFile.FileName));
            var parsed = JsonSerializer.Deserialize<JsonElement>(json);

            foreach (string field in new[] { "version", "scryptSalt", "scryptCostParam", "scryptBlockSize", "primaryMasterKey", "hmacMasterKey", "versionMac" })
            {
                Assert.True(parsed.TryGetProperty(field, out _), $"Missing field {field}");
            }

            Assert.Equal(999, parsed.GetProperty("version").GetInt32());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
