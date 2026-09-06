using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>Creates and unlocks Cryptomator format 8 vaults on a directory basis.</summary>
public static class VaultStore
{
    private const string DataFolderName = "d";

    /// <summary>
    /// Creates a new vault (masterkey file, signed vault config, and the root
    /// content directory) directly on the target directory.
    /// </summary>
    public static void Create(string vaultRootPath, string password, int scryptCostParam = VaultMasterkeyFile.DefaultScryptCostParam, int scryptBlockSize = VaultMasterkeyFile.DefaultScryptBlockSize)
    {
        if (File.Exists(Path.Combine(vaultRootPath, VaultMasterkeyFile.FileName)))
        {
            throw new VaultFormatException("A vault already exists in this directory.");
        }

        Directory.CreateDirectory(Path.Combine(vaultRootPath, DataFolderName));

        using var keys = new VaultKeys(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        VaultMasterkeyFile.Persist(vaultRootPath, keys, password, scryptCostParam, scryptBlockSize);
        VaultConfigFile.Persist(vaultRootPath, keys);

        // Root directory has the empty dirId; create its ciphertext content dir.
        CreateContentDirectory(vaultRootPath, keys, RootDirId);
    }

    /// <summary>Unlocks a vault. Throws <see cref="VaultUnlockException"/> for a wrong password.</summary>
    public static VaultSession Unlock(string vaultRootPath, string password)
    {
        VaultKeys keys = VaultMasterkeyFile.Unlock(vaultRootPath, password);
        try
        {
            VaultConfigFile.VaultConfiguration config = VaultConfigFile.LoadAndVerify(vaultRootPath, keys);
            return new VaultSession(vaultRootPath, keys, config);
        }
        catch
        {
            keys.Dispose();
            throw;
        }
    }

    internal const string RootDirId = "";

    internal static string CreateContentDirectory(string vaultRootPath, VaultKeys keys, string dirId)
    {
        string path = ContentDirectoryPath(vaultRootPath, keys, dirId);
        Directory.CreateDirectory(path);
        return path;
    }

    internal static string ContentDirectoryPath(string vaultRootPath, VaultKeys keys, string dirId)
    {
        string hash = VaultNames.HashDirectoryId(keys, dirId);
        return Path.Combine(vaultRootPath, DataFolderName, hash[..2], hash[2..]);
    }
}
