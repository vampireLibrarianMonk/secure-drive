using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>Path resolution and creation over the flattened ciphertext directory tree.</summary>
public sealed partial class VaultSession
{
    private static string[] SplitPath(string cleartextRelativePath)
    {
        string[] segments = cleartextRelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("The path must contain at least one segment.", nameof(cleartextRelativePath));
        }

        foreach (string segment in segments)
        {
            if (segment is "." or "..")
            {
                throw new ArgumentException("Path segments must not be '.' or '..'.", nameof(cleartextRelativePath));
            }
        }

        return segments;
    }

    private (string DirPath, string DirId) ResolveDirectory(ReadOnlySpan<string> segments)
    {
        if (!TryResolveDirectory(segments, out (string DirPath, string DirId) result))
        {
            throw new DirectoryNotFoundException("The archive directory does not exist.");
        }

        return result;
    }

    private bool TryResolveDirectory(ReadOnlySpan<string> segments, out (string DirPath, string DirId) result)
    {
        string dirId = VaultStore.RootDirId;
        string dirPath = VaultStore.ContentDirectoryPath(vaultRootPath, keys, dirId);

        foreach (string segment in segments)
        {
            if (!TryGetSubdirectory(dirPath, dirId, segment, create: false, out (string ChildPath, string ChildId) child))
            {
                result = default;
                return false;
            }

            dirPath = child.ChildPath;
            dirId = child.ChildId;
        }

        result = (dirPath, dirId);
        return true;
    }

    private (string DirPath, string DirId) EnsureDirectory(ReadOnlySpan<string> segments)
    {
        string dirId = VaultStore.RootDirId;
        string dirPath = VaultStore.ContentDirectoryPath(vaultRootPath, keys, dirId);

        foreach (string segment in segments)
        {
            TryGetSubdirectory(dirPath, dirId, segment, create: true, out (string ChildPath, string ChildId) child);
            dirPath = child.ChildPath;
            dirId = child.ChildId;
        }

        return (dirPath, dirId);
    }

    private bool TryGetSubdirectory(string parentDirPath, string parentDirId, string segment, bool create, out (string ChildPath, string ChildId) child)
    {
        child = default;
        string ciphertextName = VaultNames.EncryptName(keys, segment, parentDirId);
        string direct = Path.Combine(parentDirPath, ciphertextName);

        if (Directory.Exists(direct))
        {
            child = ReadCiphertextDirectory(direct, ciphertextName, parentDirId);
            return true;
        }

        string shortened = Path.Combine(parentDirPath, VaultNames.ShortenCiphertextName(ciphertextName));
        if (Directory.Exists(shortened))
        {
            child = ReadCiphertextDirectory(shortened, ciphertextName, parentDirId);
            return true;
        }

        if (!create)
        {
            return false;
        }

        string childDirId = Guid.NewGuid().ToString();
        string childPath = VaultStore.ContentDirectoryPath(vaultRootPath, keys, childDirId);
        Directory.CreateDirectory(childPath);
        Directory.CreateDirectory(direct);
        File.WriteAllText(Path.Combine(direct, VaultNames.DirectoryMarker), childDirId);
        child = (childPath, childDirId);
        return true;
    }

    private (string ChildPath, string ChildId) ReadCiphertextDirectory(string nodePath, string ciphertextName, string parentDirId)
    {
        string markerFile = Path.Combine(nodePath, VaultNames.DirectoryMarker);
        if (!File.Exists(markerFile))
        {
            throw new VaultFormatException($"Directory node {ciphertextName} is missing its {VaultNames.DirectoryMarker} marker.");
        }

        string childDirId = File.ReadAllText(markerFile);
        return (VaultStore.ContentDirectoryPath(vaultRootPath, keys, childDirId), childDirId);
    }

    private string LocateFile(string parentDirPath, string parentDirId, string fileName)
    {
        return LocateFileOrNull(parentDirPath, parentDirId, fileName)
            ?? throw new FileNotFoundException("The archive document does not exist.", fileName);
    }

    private string? LocateFileOrNull(string parentDirPath, string parentDirId, string fileName)
    {
        string ciphertextName = VaultNames.EncryptName(keys, fileName, parentDirId);
        string direct = Path.Combine(parentDirPath, ciphertextName);
        if (File.Exists(direct))
        {
            return direct;
        }

        string shortenedContents = Path.Combine(parentDirPath, VaultNames.ShortenCiphertextName(ciphertextName), ContentsFile);
        return File.Exists(shortenedContents) ? shortenedContents : null;
    }

    private void WriteFileContent(string parentDirPath, string parentDirId, string fileName, Stream content)
    {
        string ciphertextName = VaultNames.EncryptName(keys, fileName, parentDirId);
        string contentPath;

        if (ciphertextName.Length > config.ShorteningThreshold)
        {
            string shortDir = Path.Combine(parentDirPath, VaultNames.ShortenCiphertextName(ciphertextName));
            Directory.CreateDirectory(shortDir);
            File.WriteAllText(Path.Combine(shortDir, NameMappingFile), ciphertextName);
            contentPath = Path.Combine(shortDir, ContentsFile);
        }
        else
        {
            contentPath = Path.Combine(parentDirPath, ciphertextName);
        }

        // Staged write: encrypt to a sidecar name first (ignored by listing),
        // then move over the final name — a partial write never replaces a
        // good file, and unknown sidecars are skipped when listing.
        string tempPath = contentPath + ".staging";
        try
        {
            using (FileStream stream = File.Create(tempPath))
            {
                VaultContent.EncryptStream(keys, content, stream);
            }

            File.Move(tempPath, contentPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }
}
