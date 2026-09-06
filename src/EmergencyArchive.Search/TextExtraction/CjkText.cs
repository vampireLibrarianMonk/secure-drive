using System.Text;

namespace EmergencyArchive.Search.TextExtraction;

/// <summary>
/// CJK ideograph runs are single tokens for the FTS5 unicode61 tokenizer, so
/// substring searches inside CJK text would fail. Segmenting inserts a space
/// between adjacent CJK characters (both when indexing and when sanitizing
/// queries), making every CJK character a searchable token.
/// </summary>
public static class CjkText
{
    public static string Segment(string text)
    {
        if (text.Length < 2)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if (IsCjk(text[i]) && i + 1 < text.Length && IsCjk(text[i + 1]))
            {
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    public static bool IsCjk(char c)
    {
        return c is >= '\u2E80' and <= '\u9FFF'      // radicals, CJK unified ideographs
            or >= '\u3400' and <= '\u4DBF'           // extension A
            or >= '\uF900' and <= '\uFAFF'           // compatibility ideographs
            or >= '\u3040' and <= '\u30FF'           // hiragana / katakana
            or >= '\uAC00' and <= '\uD7AF';          // hangul syllables
    }
}
