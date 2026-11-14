namespace EmergencyArchive.Crypto.Vault;

/// <summary>Base exception for vault errors that are safe to show to users.</summary>
public class VaultException : Exception
{
    public VaultException(string message) : base(message) { }
    public VaultException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The archive password is wrong (or the vault does not belong to this password).</summary>
public sealed class VaultUnlockException : VaultException
{
    public VaultUnlockException()
        : base("Unable to unlock the archive. Check the password and try again.") { }
}

/// <summary>The vault structure is unreadable (damaged, tampered, or unsupported format).</summary>
public sealed class VaultFormatException : VaultException
{
    public VaultFormatException(string message) : base(message) { }
    public VaultFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>A vault element failed authentication (corrupted or modified ciphertext).</summary>
public sealed class VaultIntegrityException : VaultException
{
    public VaultIntegrityException(string message) : base(message) { }
    public VaultIntegrityException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The archive is already open in another application instance (single-instance
/// guard, red/blue cycle 2026-11). Concurrent writers could interleave updates
/// and only be caught by VERIFY after the fact, so the second open is refused.
/// </summary>
public sealed class VaultInUseException : VaultException
{
    public VaultInUseException(string message) : base(message) { }
    public VaultInUseException(string message, Exception innerException) : base(message, innerException) { }
}
