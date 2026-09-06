namespace EmergencyArchive.Core;

/// <summary>
/// Default document categories (spec section 10). Categories are configurable
/// in Setup Mode; users are never required to manually categorize every document.
/// </summary>
public static class DocumentCategories
{
    public static readonly IReadOnlyList<string> Default =
    [
        "Identity",
        "Financial",
        "Insurance",
        "Property",
        "Legal",
        "Medical",
        "Education",
        "Emergency",
        "Family",
        "Other",
    ];
}
