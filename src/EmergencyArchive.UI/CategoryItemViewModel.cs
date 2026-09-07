namespace EmergencyArchive.UI;

/// <summary>One entry of the folder sidebar: a top-level folder and its document count.</summary>
public sealed record CategoryItemViewModel(string Name, int Count)
{
    public const string AllName = "All documents";

    public bool IsAll => Name == AllName;

    public string DisplayName => $"{Name}  ({Count:N0})";
}
