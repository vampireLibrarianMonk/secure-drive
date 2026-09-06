using System.Security.Cryptography;
using System.Text;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>
/// Filename encryption and directory ID handling per the vault format:
/// names are NFC-normalized, AES-SIV-encrypted with the parent directory ID as
/// associated data, and stored as padded base64url with a .c9r suffix;
/// directories are flattened to base32(sha1(aesSiv(dirId))) paths.
/// </summary>
public static class VaultNames
{
    public const string CiphertextSuffix = ".c9r";
    public const string ShortenedSuffix = ".c9s";
    public const string DirectoryMarker = "dir.c9r";
    public const string DirectoryIdBackup = "dirid.c9r";

    /// <summary>Encrypts a file name bound to its parent directory ID.</summary>
    public static string EncryptName(VaultKeys keys, string cleartextName, string parentDirId)
    {
        string normalized = cleartextName.Normalize(NormalizationForm.FormC);
        byte[] combined = VaultPrimitives.CombineKeys(keys);
        byte[] ciphertext = Rfc5297Siv.Encrypt(combined, Encoding.UTF8.GetBytes(normalized), Encoding.UTF8.GetBytes(parentDirId));
        return VaultEncoding.EncodeBase64Url(ciphertext) + CiphertextSuffix;
    }

    /// <summary>Decrypts a ciphertext name; throws on wrong key, foreign directory, or corruption.</summary>
    public static string DecryptName(VaultKeys keys, string ciphertextName, string parentDirId)
    {
        string base64 = StripSuffix(ciphertextName);
        byte[] combined = VaultPrimitives.CombineKeys(keys);
        if (!Rfc5297Siv.TryDecrypt(combined, VaultEncoding.DecodeBase64Url(base64), out byte[] plaintext, Encoding.UTF8.GetBytes(parentDirId)))
        {
            throw new VaultIntegrityException("File name failed authentication (wrong key or foreign directory).");
        }

        return Encoding.UTF8.GetString(plaintext).Normalize(NormalizationForm.FormC);
    }

    /// <summary>The on-storage path segment of a directory: base32(sha1(aesSiv(dirId))).</summary>
    public static string HashDirectoryId(VaultKeys keys, string dirId)
    {
        byte[] combined = VaultPrimitives.CombineKeys(keys);
        byte[] encrypted = Rfc5297Siv.Encrypt(combined, Encoding.UTF8.GetBytes(dirId));
        byte[] hash = SHA1.HashData(encrypted);
        return VaultEncoding.EncodeBase32(hash);
    }

    /// <summary>base64url(sha1(ciphertextName)) + .c9s — directory used when a ciphertext name is too long.</summary>
    public static string ShortenCiphertextName(string ciphertextName)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(ciphertextName));
        return VaultEncoding.EncodeBase64Url(hash) + ShortenedSuffix;
    }

    public static string StripSuffix(string ciphertextName)
    {
        if (ciphertextName.EndsWith(CiphertextSuffix, StringComparison.Ordinal))
        {
            return ciphertextName[..^CiphertextSuffix.Length];
        }

        if (ciphertextName.EndsWith(ShortenedSuffix, StringComparison.Ordinal))
        {
            return ciphertextName[..^ShortenedSuffix.Length];
        }

        return ciphertextName;
    }
}
