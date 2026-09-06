using System.Text;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>
/// Encodings used by the Cryptomator vault format:
/// padded Base64url for ciphertext names (Guava base64Url-compatible) and
/// padded RFC 4648 Base32 for directory ID hashes.
/// </summary>
public static class VaultEncoding
{
    /// <summary>Base64url with '=' padding (A-Za-z0-9-_+=), as used for ciphertext file names.</summary>
    public static string EncodeBase64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
    }

    public static byte[] DecodeBase64Url(string text)
    {
        string b64 = text.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 0: break;
            default: throw new FormatException("Invalid base64url length.");
        }

        return Convert.FromBase64String(b64);
    }

    /// <summary>Unpadded base64url per RFC 7515 (JWT segments).</summary>
    public static string EncodeJwtSegment(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] DecodeJwtSegment(string segment)
    {
        return DecodeBase64Url(segment);
    }

    /// <summary>RFC 4648 Base32 (A-Z, 2-7) with '=' padding.</summary>
    public static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int bitBuffer = 0;
        int bits = 0;

        foreach (byte b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(alphabet[(bitBuffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(alphabet[(bitBuffer << (5 - bits)) & 0x1F]);
        }

        while (sb.Length % 8 != 0)
        {
            sb.Append('=');
        }

        return sb.ToString();
    }
}
