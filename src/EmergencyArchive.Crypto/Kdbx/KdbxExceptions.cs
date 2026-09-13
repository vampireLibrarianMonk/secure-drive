namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>Base exception for KDBX (KeePass) import/export errors safe to show users.</summary>
public class KdbxException : Exception
{
    public KdbxException(string message) : base(message) { }
    public KdbxException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The KDBX file is not a recognised/supported KeePass database.</summary>
public sealed class KdbxFormatException : KdbxException
{
    public KdbxFormatException(string message) : base(message) { }
    public KdbxFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The password (or key) is wrong, or the file has been tampered with.</summary>
public sealed class KdbxAuthenticationException : KdbxException
{
    public KdbxAuthenticationException()
        : base("Could not open the KeePass file. Check the password and try again.") { }

    public KdbxAuthenticationException(string message) : base(message) { }
}
