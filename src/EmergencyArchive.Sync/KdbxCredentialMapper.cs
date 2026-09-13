using EmergencyArchive.Crypto.Kdbx;

namespace EmergencyArchive.Sync;

/// <summary>
/// Bridges the KeePass <see cref="KdbxDatabase"/> shape and the app's own
/// <see cref="CredentialDatabase"/>. KeePass groups are flattened; the standard
/// KDBX fields (Title/UserName/Password) map to Site/Username/Password, and
/// URL, Notes, and any custom string fields are carried as credential extras so
/// nothing is silently lost. Export reverses the mapping so a round trip
/// through KeePass keeps URL/Notes as first-class KDBX fields.
/// </summary>
public static class KdbxCredentialMapper
{
    /// <summary>Extra-field key used to carry a KDBX entry's URL.</summary>
    public const string UrlKey = "URL";

    /// <summary>Extra-field key used to carry a KDBX entry's Notes.</summary>
    public const string NotesKey = "Notes";

    /// <summary>
    /// Reads a KeePass file (KDBX 3.1 or 4.x, password only) and converts every
    /// entry into a credential with a fresh id. Throws the same exceptions as
    /// <see cref="KdbxCodec.Read"/> (wrong password / not a valid KDBX file).
    /// </summary>
    public static IReadOnlyList<Credential> Import(byte[] kdbxBytes, string password)
    {
        KdbxDatabase kdbx = KdbxCodec.Read(kdbxBytes, password);
        var credentials = new List<Credential>(kdbx.Entries.Count);
        foreach (KdbxEntry entry in kdbx.Entries)
        {
            credentials.Add(ToCredential(entry));
        }

        return credentials;
    }

    /// <summary>Exports the credential database as a KDBX 4 file (Argon2id + AES-256-CBC).</summary>
    public static byte[] Export(CredentialDatabase database, string password)
    {
        var kdbx = new KdbxDatabase();
        foreach (Credential credential in database.Entries)
        {
            kdbx.Entries.Add(ToKdbxEntry(credential));
        }

        return KdbxCodec.Write(kdbx, password);
    }

    private static Credential ToCredential(KdbxEntry entry)
    {
        var extras = new List<CredentialField>();
        if (!string.IsNullOrEmpty(entry.Url))
        {
            extras.Add(new CredentialField(UrlKey, entry.Url));
        }

        if (!string.IsNullOrEmpty(entry.Notes))
        {
            extras.Add(new CredentialField(NotesKey, entry.Notes));
        }

        foreach (KeyValuePair<string, string> field in entry.CustomFields)
        {
            // Skip blank keys and duplicates of the standard-mapped extras.
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                continue;
            }

            extras.Add(new CredentialField(field.Key, field.Value ?? string.Empty));
        }

        return new Credential(
            Guid.NewGuid().ToString("N"),
            entry.Title ?? string.Empty,
            entry.UserName ?? string.Empty,
            entry.Password ?? string.Empty,
            extras);
    }

    private static KdbxEntry ToKdbxEntry(Credential credential)
    {
        var entry = new KdbxEntry
        {
            Title = credential.Site,
            UserName = credential.Username,
            Password = credential.Password,
        };

        foreach (CredentialField field in credential.EffectiveExtras)
        {
            if (string.Equals(field.Key, UrlKey, StringComparison.OrdinalIgnoreCase))
            {
                entry.Url = field.Value;
            }
            else if (string.Equals(field.Key, NotesKey, StringComparison.OrdinalIgnoreCase))
            {
                entry.Notes = field.Value;
            }
            else
            {
                entry.CustomFields.Add(new KeyValuePair<string, string>(field.Key, field.Value));
            }
        }

        return entry;
    }
}
