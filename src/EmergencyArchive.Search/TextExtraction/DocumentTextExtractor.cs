using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EmergencyArchive.Search.TextExtraction;

/// <summary>
/// Extracts searchable plain text from supported document types (spec
/// section 8): PDF, TXT, Markdown, DOCX, XLSX, PPTX, HTML. Extraction is
/// best-effort and resilient: an unreadable document yields null instead of
/// an exception, so a single bad file never breaks indexing. Extracted text
/// is capped to keep the index bounded.
/// </summary>
public static class DocumentTextExtractor
{
    public const int DefaultMaxCharacters = 1_000_000;
    private const int MaxRawBytes = 8 * 1024 * 1024;

    public static string? Extract(string fileName, Stream content, int maxCharacters = DefaultMaxCharacters)
    {
        try
        {
            return Path.GetExtension(fileName.ToLowerInvariant()) switch
            {
                ".txt" or ".md" or ".markdown" or ".log" or ".csv" => ReadAsText(content, maxCharacters),
                ".html" or ".htm" => ExtractHtml(content, maxCharacters),
                ".docx" => OoxmlTextExtractor.Extract(content, fullName => fullName == "word/document.xml", "t", "p", maxCharacters),
                ".xlsx" or ".xlsm" => OoxmlTextExtractor.Extract(content, fullName => fullName == "xl/sharedStrings.xml", "t", "si", maxCharacters),
                ".pptx" => OoxmlTextExtractor.Extract(content,
                    fullName => fullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && fullName.EndsWith(".xml", StringComparison.Ordinal),
                    "t", "p", maxCharacters),
                ".pdf" => ExtractPdf(content, maxCharacters),
                _ => null,
            };
        }
        catch (Exception)
        {
            // Extraction must never break indexing (spec section 24): an
            // unreadable, corrupt, or mislabeled document is indexed by name
            // only and reported by archive verification (Phase 3).
            return null;
        }
    }

    /// <summary>Plain text files: strict UTF-8 first, Latin-1 fallback for legacy files.</summary>
    private static string? ReadAsText(Stream content, int maxCharacters)
    {
        byte[] bytes = ReadBytes(content);
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(bytes);
        }

        text = text.Trim();
        return text.Length == 0 ? null : text[..Math.Min(text.Length, maxCharacters)];
    }

    private static string? ExtractHtml(Stream content, int maxCharacters)
    {
        string? raw = ReadAsText(content, maxCharacters);
        if (raw is null)
        {
            return null;
        }

        // Drop scripts/styles, strip tags, decode entities, collapse whitespace.
        raw = Regex.Replace(raw, "<script\\b[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        raw = Regex.Replace(raw, "<style\\b[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        raw = Regex.Replace(raw, "<[^>]+>", " ");
        raw = WebUtility.HtmlDecode(raw);
        raw = Regex.Replace(raw, "\\s+", " ").Trim();

        return raw.Length == 0 ? null : raw[..Math.Min(raw.Length, maxCharacters)];
    }

    private static string? ExtractPdf(Stream content, int maxCharacters)
    {
        using var document = PdfDocument.Open(content);
        var sb = new StringBuilder();
        foreach (Page page in document.GetPages())
        {
            foreach (Word word in page.GetWords())
            {
                sb.Append(word.Text).Append(' ');
                if (sb.Length >= maxCharacters)
                {
                    break;
                }
            }

            sb.Append('\n');
            if (sb.Length >= maxCharacters)
            {
                break;
            }
        }

        var text = sb.ToString().Trim();
        return text.Length == 0 ? null : text[..Math.Min(text.Length, maxCharacters)];
    }

    private static byte[] ReadBytes(Stream content)
    {
        using var target = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        int total = 0;
        while (total < MaxRawBytes && (read = content.Read(buffer, 0, Math.Min(buffer.Length, MaxRawBytes - total))) > 0)
        {
            target.Write(buffer, 0, read);
            total += read;
        }

        return target.ToArray();
    }
}
