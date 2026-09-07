namespace EmergencyArchive.Core;

/// <summary>
/// Locates the archive vault relative to the application (spec section 4 drive
/// layout: <c>app\windows\START-WINDOWS.exe</c> with <c>vault\</c> at the drive
/// root) or via the <c>EMERGENCY_ARCHIVE_VAULT_PATH</c> environment variable
/// (development override; a path is not secret material, spec section 19).
/// </summary>
public static class VaultLocator
{
    public const string VaultFolderName = "vault";
    public const string VaultMarkerFile = "masterkey.cryptomator";
    public const string VaultPathEnvironmentVariable = "EMERGENCY_ARCHIVE_VAULT_PATH";

    /// <summary>
    /// Returns the vault directory, or null when no vault is found. Walks up
    /// from <paramref name="baseDirectory"/> (default: the executable's
    /// directory) looking for a <c>vault\masterkey.cryptomator</c>.
    /// </summary>
    public static string? Locate(string? baseDirectory = null, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        string? overridePath = environment(VaultPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath) && IsVaultRoot(overridePath))
        {
            return overridePath;
        }

        string? directory = baseDirectory ?? AppContext.BaseDirectory;
        for (int level = 0; level < 6 && !string.IsNullOrEmpty(directory); level++)
        {
            string candidate = Path.Combine(directory, VaultFolderName);
            if (IsVaultRoot(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return null;
    }

    private static bool IsVaultRoot(string path) => File.Exists(Path.Combine(path, VaultMarkerFile));

    /// <summary>
    /// Returns the directory where a NEW vault should be created for first-use
    /// setup (the in-app "create archive" flow). This is the drive/media root
    /// that holds the application, so the layout matches spec section 4
    /// (<c>&lt;root&gt;\vault\</c> beside <c>app\windows\START-WINDOWS.exe</c>).
    /// An <c>EMERGENCY_ARCHIVE_VAULT_PATH</c> override wins (development).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Locate"/>, this never requires the vault to exist yet.
    /// From the executable directory it walks up to the drive/volume root and
    /// returns <c>&lt;root&gt;\vault</c>. The returned directory may or may not
    /// already contain a vault; callers check that before creating.
    /// </remarks>
    public static string LocateCreatable(string? baseDirectory = null, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        string? overridePath = environment(VaultPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        string directory = baseDirectory ?? AppContext.BaseDirectory;

        // Walk up to the volume/drive root so the vault lands beside the app at
        // the drive top level (D:\vault), regardless of app\windows nesting.
        string root = Path.GetPathRoot(Path.GetFullPath(directory)) ?? directory;
        return Path.Combine(root, VaultFolderName);
    }
}
