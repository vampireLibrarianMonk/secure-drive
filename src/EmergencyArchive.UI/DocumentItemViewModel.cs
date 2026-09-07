using System.IO;

namespace EmergencyArchive.UI;

/// <summary>One document shown in the archive list; identified by its vault-relative path.</summary>
public sealed record DocumentItemViewModel(string RelativePath)
{
    public string Name => Path.GetFileName(RelativePath);

    /// <summary>Vault-relative folder shown under the name ("&#x2F;" for the archive root).</summary>
    public string Folder
    {
        get
        {
            string? directory = Path.GetDirectoryName(RelativePath);
            return string.IsNullOrEmpty(directory) ? "/" : directory.Replace('\\', '/');
        }
    }

    public bool HasFolder => Folder != "/";

    /// <summary>Top-level folder — the archive category (spec section 10).</summary>
    public string Category
    {
        get
        {
            string[] parts = RelativePath.Split('/', '\\');
            return parts.Length > 1 ? parts[0] : "(no category)";
        }
    }

    /// <summary>Set for full-text search results; shows a content excerpt under the name.</summary>
    public string? Snippet { get; init; }

    public bool HasSnippet => !string.IsNullOrEmpty(Snippet);
}
