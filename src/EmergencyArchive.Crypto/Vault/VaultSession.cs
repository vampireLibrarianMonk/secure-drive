using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Vault;

public enum VaultEntryKind
{
    File,
    Directory,
}

public sealed record VaultEntry(string Name, VaultEntryKind Kind);

/// <summary>
/// An unlocked vault: lists, reads, and writes encrypted documents without
/// mounting anything. Dispose locks the vault (clears key material).
/// </summary>
public sealed partial class VaultSession : IDisposable
{
    private const string NameMappingFile = "name.c9s";
    private const string ContentsFile = "contents.c9r";

    private readonly string vaultRootPath;
    private readonly VaultKeys keys;
    private readonly VaultConfigFile.VaultConfiguration config;
    private bool disposed;

    internal VaultSession(string vaultRootPath, VaultKeys keys, VaultConfigFile.VaultConfiguration config)
    {
        this.vaultRootPath = vaultRootPath;
        this.keys = keys;
        this.config = config;
    }

    public string VaultId => config.VaultId;

    public string VaultRootPath => vaultRootPath;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        keys.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    /// <summary>Lists the entries of a cleartext-relative directory (empty = vault root).</summary>
    public IReadOnlyList<VaultEntry> List(string cleartextRelativeDir = "")
    {
        ThrowIfDisposed();
        (string ciphertextDir, string dirId) = string.IsNullOrWhiteSpace(cleartextRelativeDir)
            ? (VaultStore.ContentDirectoryPath(vaultRootPath, keys, VaultStore.RootDirId), VaultStore.RootDirId)
            : ResolveDirectory(SplitPath(cleartextRelativeDir));
        var entries = new List<VaultEntry>();

        foreach (FileSystemInfo info in new DirectoryInfo(ciphertextDir).EnumerateFileSystemInfos())
        {
            string ciphertextName = info.Name;
            if (ciphertextName is VaultNames.DirectoryMarker or VaultNames.DirectoryIdBackup)
            {
                continue; // internal markers of this directory
            }

            if (info is DirectoryInfo directory && ciphertextName.EndsWith(VaultNames.ShortenedSuffix, StringComparison.Ordinal))
            {
                // Shortened node: name.c9s holds the full ciphertext name.
                string mappingFile = Path.Combine(directory.FullName, NameMappingFile);
                if (!File.Exists(mappingFile))
                {
                    throw new VaultFormatException($"Shortened node {ciphertextName} is missing its name mapping file.");
                }

                string fullCiphertextName = File.ReadAllText(mappingFile);
                VaultEntryKind kind = File.Exists(Path.Combine(directory.FullName, ContentsFile))
                    ? VaultEntryKind.File
                    : VaultEntryKind.Directory;
                entries.Add(new VaultEntry(VaultNames.DecryptName(keys, fullCiphertextName, dirId), kind));
            }
            else if (info is DirectoryInfo plainDirectory && ciphertextName.EndsWith(VaultNames.CiphertextSuffix, StringComparison.Ordinal))
            {
                if (!File.Exists(Path.Combine(plainDirectory.FullName, VaultNames.DirectoryMarker)))
                {
                    throw new VaultFormatException($"Directory {ciphertextName} has no {VaultNames.DirectoryMarker} marker.");
                }

                entries.Add(new VaultEntry(VaultNames.DecryptName(keys, ciphertextName, dirId), VaultEntryKind.Directory));
            }
            else if (info is FileInfo && ciphertextName.EndsWith(VaultNames.CiphertextSuffix, StringComparison.Ordinal))
            {
                entries.Add(new VaultEntry(VaultNames.DecryptName(keys, ciphertextName, dirId), VaultEntryKind.File));
            }
            // Entries without a known suffix are ignored (not part of the vault).
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return entries;
    }

    /// <summary>Writes (or overwrites) a file at a cleartext-relative path.</summary>
    public void WriteFile(string cleartextRelativePath, Stream content)
    {
        ThrowIfDisposed();
        string[] segments = SplitPath(cleartextRelativePath);
        (string ciphertextDir, string dirId) = EnsureDirectory(segments.AsSpan(0, segments.Length - 1));
        WriteFileContent(ciphertextDir, dirId, segments[^1], content);
    }

    public void WriteFile(string cleartextRelativePath, byte[] content) =>
        WriteFile(cleartextRelativePath, new MemoryStream(content, writable: false));

    /// <summary>Reads a file into a target stream, authenticating every chunk.</summary>
    public void ReadFile(string cleartextRelativePath, Stream target)
    {
        ThrowIfDisposed();
        string[] segments = SplitPath(cleartextRelativePath);
        (string ciphertextDir, string dirId) = ResolveDirectory(segments.AsSpan(0, segments.Length - 1));
        string contentPath = LocateFile(ciphertextDir, dirId, segments[^1]);

        using FileStream stream = File.OpenRead(contentPath);
        VaultContent.DecryptStream(keys, stream, target);
    }

    public byte[] ReadFile(string cleartextRelativePath)
    {
        using var target = new MemoryStream();
        ReadFile(cleartextRelativePath, target);
        return target.ToArray();
    }

    public bool FileExists(string cleartextRelativePath)
    {
        ThrowIfDisposed();
        string[] segments = SplitPath(cleartextRelativePath);
        if (!TryResolveDirectory(segments.AsSpan(0, segments.Length - 1), out (string Dir, string DirId) parent))
        {
            return false;
        }

        return File.Exists(LocateFileOrNull(parent.Dir, parent.DirId, segments[^1]) ?? string.Empty);
    }

    /// <summary>Creates a directory (and any missing parents) at a cleartext-relative path.</summary>
    public void CreateDirectory(string cleartextRelativeDir)
    {
        ThrowIfDisposed();
        EnsureDirectory(SplitPath(cleartextRelativeDir));
    }

    /// <summary>
    /// Recursively enumerates every stored document as a cleartext-relative
    /// path (forward-slash separated), e.g. <c>Insurance/Home policy.pdf</c>.
    /// </summary>
    public IEnumerable<string> EnumerateFiles(string cleartextRelativeDir = "")
    {
        ThrowIfDisposed();
        return EnumerateFilesCore(string.IsNullOrWhiteSpace(cleartextRelativeDir) ? string.Empty : cleartextRelativeDir.Trim());
    }

    private IEnumerable<string> EnumerateFilesCore(string relativeDir)
    {
        foreach (VaultEntry entry in List(relativeDir))
        {
            string childPath = relativeDir.Length == 0 ? entry.Name : $"{relativeDir}/{entry.Name}";
            if (entry.Kind == VaultEntryKind.Directory)
            {
                foreach (string nested in EnumerateFilesCore(childPath))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return childPath;
            }
        }
    }
}
