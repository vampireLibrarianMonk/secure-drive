namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// Public entry point for KeePass KDBX import/export. Reads KDBX 3.1 and 4.x
/// (password only) and writes KDBX 4. This layer works in the neutral
/// <see cref="KdbxDatabase"/> shape; mapping to the app's credential model is
/// done by the caller. MIT-clean, built on BouncyCastle (no GPL code).
/// </summary>
public static class KdbxCodec
{
    /// <summary>
    /// Decrypts and parses a KDBX file. Throws <see cref="KdbxAuthenticationException"/>
    /// for a wrong password and <see cref="KdbxFormatException"/> for a file that
    /// is not a supported/valid KDBX database.
    /// </summary>
    public static KdbxDatabase Read(byte[] fileBytes, string password)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        ArgumentNullException.ThrowIfNull(password);

        var reader = new KdbxReader(fileBytes);
        KdbxHeader header = KdbxHeader.Parse(reader);

        if (header.IsVersion4)
        {
            return Kdbx4Reader.Read(reader, header, password);
        }

        if (header.IsVersion3)
        {
            return Kdbx3Reader.Read(reader, header, password);
        }

        throw new KdbxFormatException($"Unsupported KDBX major version {header.MajorVersion}.");
    }

    /// <summary>Encrypts a database as a KDBX 4 file (Argon2id + AES-256-CBC + GZip).</summary>
    public static byte[] Write(KdbxDatabase database, string password)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(password);
        return Kdbx4Writer.Write(database, password);
    }
}
