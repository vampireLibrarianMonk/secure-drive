namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// A single KeePass entry, flattened out of the group tree. Standard string
/// fields (Title/UserName/Password/URL/Notes) are surfaced directly; any other
/// string fields are kept in <see cref="CustomFields"/>. This is the neutral
/// shape the reader produces and the writer consumes; the app-facing mapping to
/// the credential model lives one layer up.
/// </summary>
public sealed class KdbxEntry
{
    public string Title { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>Non-standard string fields (key -> value), e.g. TOTP, recovery codes.</summary>
    public List<KeyValuePair<string, string>> CustomFields { get; } = new();
}

/// <summary>A decoded KeePass database: a flat list of entries (groups are flattened).</summary>
public sealed class KdbxDatabase
{
    public List<KdbxEntry> Entries { get; } = new();
}
