using System.Security.Cryptography;

namespace EmergencyArchive.Crypto.Vault;

/// <summary>
/// The two 256-bit masterkeys of a vault, held in memory only while unlocked.
/// Dispose clears the key material (spec section 19: clear sensitive buffers).
/// </summary>
public sealed class VaultKeys : IDisposable
{
    private byte[] encryptionKey;
    private byte[] macKey;
    private bool disposed;

    public VaultKeys(byte[] encryptionKey, byte[] macKey)
    {
        if (encryptionKey.Length != 32 || macKey.Length != 32)
        {
            throw new ArgumentException("Vault masterkeys must be 256-bit each.");
        }

        this.encryptionKey = encryptionKey;
        this.macKey = macKey;
    }

    public ReadOnlySpan<byte> EncryptionKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return encryptionKey;
        }
    }

    public ReadOnlySpan<byte> MacKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return macKey;
        }
    }

    /// <summary>The 64-byte concatenation encKey||macKey used to verify vault.cryptomator.</summary>
    public byte[] RawMasterkey()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] raw = new byte[64];
        encryptionKey.CopyTo(raw, 0);
        macKey.CopyTo(raw, 32);
        return raw;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CryptographicOperations.ZeroMemory(encryptionKey);
        CryptographicOperations.ZeroMemory(macKey);
    }
}
