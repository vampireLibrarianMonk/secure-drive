using System.IO.Compression;
using System.Text;
using System.Xml;

namespace EmergencyArchive.Search.TextExtraction;

/// <summary>
/// Text extraction for OOXML packages (DOCX/XLSX/PPTX): zip packages of XML
/// whose visible text lives in simple text elements. The XML reader disables
/// DTDs and external resolution entirely (XXE hardening).
/// </summary>
internal static class OoxmlTextExtractor
{
    public static string? Extract(Stream content, Func<string, bool> entryFilter, string textElement, string paragraphElement, int maxCharacters)
    {
        using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var sb = new StringBuilder();
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!entryFilter(entry.FullName))
            {
                continue;
            }

            using Stream entryStream = entry.Open();
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(entryStream, settings);

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == textElement)
                {
                    sb.Append(reader.ReadElementContentAsString());
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == paragraphElement)
                {
                    sb.Append('\n');
                }

                if (sb.Length >= maxCharacters)
                {
                    break;
                }
            }

            if (sb.Length >= maxCharacters)
            {
                break;
            }
        }

        var text = sb.ToString().Trim();
        return text.Length == 0 ? null : text[..Math.Min(text.Length, maxCharacters)];
    }
}
