using System.Security.Cryptography;

namespace EmergencyArchive.Integrity;

/// <summary>
/// SHA-256 hashing used by the manifest and archive verification (spec sections
/// 15 and 16). Only the standard library implementation is used.
/// </summary>
public static class Sha256
{
    /// <summary>Computes the lowercase hex SHA-256 digest of the stream (consumes the stream).</summary>
    public static string ComputeHash(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Computes the lowercase hex SHA-256 digest of a file's contents.</summary>
    public static string ComputeFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return ComputeHash(stream);
    }
}
