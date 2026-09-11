namespace EmergencyArchive.Sync;

/// <summary>
/// One extra field on a credential: an arbitrary key/value pair (recovery
/// codes, PIN, security questions, notes, etc.). Values are secret by default
/// at the presentation layer (obfuscated with reveal/copy).
/// </summary>
public sealed record CredentialField(string Key, string Value);

/// <summary>
/// One stored credential: a site, a username, a password, and any number of
/// extra key/value fields. The password and extra values are the secrets.
/// </summary>
public sealed record Credential(
    string Id,
    string Site,
    string Username,
    string Password,
    IReadOnlyList<CredentialField> Extras)
{
    /// <summary>Creates a new credential with a fresh id and no extras.</summary>
    public static Credential Create(string site, string username, string password) =>
        new(Guid.NewGuid().ToString("N"), site, username, password, []);

    public IReadOnlyList<CredentialField> EffectiveExtras => Extras ?? [];
}

/// <summary>
/// The set of stored credentials — the app's own MIT-clean credential store,
/// persisted as an encrypted document inside the vault (see
/// <see cref="CredentialStore"/>). This is deliberately a small, documented
/// model of our own; it is not the KeePass/KDBX format (KDBX import/export is a
/// separate, self-implemented step so no GPL code is linked).
/// </summary>
public sealed record CredentialDatabase(IReadOnlyList<Credential> Entries)
{
    public static readonly CredentialDatabase Empty = new([]);

    public bool IsEmpty => Entries.Count == 0;

    public int Count => Entries.Count;

    /// <summary>Adds a credential, or replaces the one with the same id.</summary>
    public CredentialDatabase With(Credential credential)
    {
        var others = Entries.Where(c => !string.Equals(c.Id, credential.Id, StringComparison.Ordinal));
        return new CredentialDatabase([.. others, credential]);
    }

    /// <summary>Removes the credential with the given id (no-op if absent).</summary>
    public CredentialDatabase Without(string id)
    {
        return new CredentialDatabase([.. Entries.Where(c => !string.Equals(c.Id, id, StringComparison.Ordinal))]);
    }

    /// <summary>Finds a credential by id, or null.</summary>
    public Credential? Find(string id) =>
        Entries.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
}
