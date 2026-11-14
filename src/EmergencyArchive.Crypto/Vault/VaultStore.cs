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
            FileStream? driveLock = AcquireVaultLock(vaultRootPath);
            if (driveLock is null)
            {
                return new VaultSession(vaultRootPath, keys, config);
            }

            try
            {
                return new VaultSession(vaultRootPath, keys, config, driveLock);
            }
            catch
            {
                driveLock.Dispose();
                throw;
            }
        }
        catch
        {
            keys.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Single-instance guard (red/blue cycle 2026-11): holds an exclusive
    /// FileShare.None handle on a lock file INSIDE the vault directory for the
    /// lifetime of the session. A second application instance opening the same
    /// archive fails with <see cref="VaultInUseException"/> instead of racing
    /// the first one's manifest and index writes (which VERIFY would only
    /// catch after the fact). The lock lives per-vault (not at the drive root,
    /// where unrelated vaults — or parallel test vaults under a shared temp
    /// directory — would collide). Cryptomator ignores unknown files in the
    /// vault root; the interop drill confirms this. Returns null on read-only
    /// media, where the guard cannot be created — and where concurrent access
    /// is read-only anyway.
    /// </summary>
    private static FileStream? AcquireVaultLock(string vaultRootPath)
    {
        string lockPath = Path.Combine(vaultRootPath, ".emergency-archive-lock");
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e)
        {
            throw new VaultInUseException(
                "This archive is already open on this computer. Close the other Emergency Archive window first.",
                e);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
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
