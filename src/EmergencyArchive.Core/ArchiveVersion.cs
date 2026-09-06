namespace EmergencyArchive.Core;

/// <summary>
/// Immutable archive version identifier in the format <c>YYYY.MM.DD.sequence</c>
/// (spec section 18). Every completed archive update receives exactly one of these.
/// </summary>
public readonly record struct ArchiveVersion(int Year, int Month, int Day, int Sequence)
    : IComparable<ArchiveVersion>
{
    /// <summary>Creates a version from a timestamp and a 1-based daily sequence number.</summary>
    public static ArchiveVersion Create(DateTimeOffset timestamp, int sequence)
    {
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "The sequence number starts at 1.");
        }

        return new ArchiveVersion(timestamp.Year, timestamp.Month, timestamp.Day, sequence);
    }

    public override string ToString() => $"{Year:D4}.{Month:D2}.{Day:D2}.{Sequence:D3}";

    public static bool TryParse(string? text, out ArchiveVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('.');
        if (parts.Length != 4
            || !int.TryParse(parts[0], out int year)
            || !int.TryParse(parts[1], out int month)
            || !int.TryParse(parts[2], out int day)
            || !int.TryParse(parts[3], out int sequence))
        {
            return false;
        }

        if (year is < 1 or > 9999
            || month is < 1 or > 12
            || day is < 1 or > 31
            || sequence < 1)
        {
            return false;
        }

        version = new ArchiveVersion(year, month, day, sequence);
        return true;
    }

    public int CompareTo(ArchiveVersion other)
    {
        int result = Year.CompareTo(other.Year);
        if (result != 0) return result;
        result = Month.CompareTo(other.Month);
        if (result != 0) return result;
        result = Day.CompareTo(other.Day);
        if (result != 0) return result;
        return Sequence.CompareTo(other.Sequence);
    }
}
