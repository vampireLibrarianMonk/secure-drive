using System.IO;

namespace EmergencyArchive.UI;

/// <summary>One document shown in the archive list; identified by its vault-relative path.</summary>
public sealed record DocumentItemViewModel(string RelativePath)
{
    public string Name => Path.GetFileName(RelativePath);

    /// <summary>Set for full-text search results; shows a content excerpt under the name.</summary>
    public string? Snippet { get; init; }

    public bool HasSnippet => !string.IsNullOrEmpty(Snippet);
}
