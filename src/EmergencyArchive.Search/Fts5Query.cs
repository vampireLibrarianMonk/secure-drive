using System.Text;

namespace EmergencyArchive.Search;

/// <summary>
/// Builds safe FTS5 <c>MATCH</c> expressions from raw user input (spec section 8).
/// </summary>
/// <remarks>
/// User input is never interpolated into the query string directly: every
/// whitespace-separated term is wrapped in double quotes with embedded quotes
/// doubled, which neutralizes FTS5 syntax characters and keeps the search box
/// forgiving for a stressed user ("home insurance" simply works).
/// </remarks>
public static class Fts5Query
{
    /// <summary>Converts raw user input into a quoted-terms MATCH expression. Empty input yields an empty string.</summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string[] terms = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        StringBuilder expression = new();
        for (int i = 0; i < terms.Length; i++)
        {
            if (i > 0)
            {
                expression.Append(' ');
            }

            expression.Append('"').Append(terms[i].Replace("\"", "\"\"")).Append('"');
        }

        return expression.ToString();
    }
}
