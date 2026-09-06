using EmergencyArchive.Search.TextExtraction;
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
    /// <remarks>
    /// Each term becomes a quoted prefix query (<c>"term"*</c>): partial words
    /// match (spec section 26 — partial filename search) while all FTS5 syntax
    /// characters stay inert. Adjacent terms are implicitly AND-ed.
    /// </remarks>
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

            // Segment CJK runs so every character becomes a searchable token
            // (the unicode61 tokenizer keeps CJK runs as single tokens).
            string term = CjkText.Segment(terms[i]);
            expression.Append('"').Append(term.Replace("\"", "\"\"")).Append("\"*");
        }

        return expression.ToString();
    }
}
