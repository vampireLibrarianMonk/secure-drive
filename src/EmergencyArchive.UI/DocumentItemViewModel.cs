using System.IO;

namespace EmergencyArchive.UI;

/// <summary>One document shown in the archive list; identified by its vault-relative path.</summary>
public sealed record DocumentItemViewModel(string RelativePath)
{
    public string Name => Path.GetFileName(RelativePath);
}
