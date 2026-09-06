using System.IO.Compression;
using System.Text;
using EmergencyArchive.Search.TextExtraction;
using Xunit;

namespace EmergencyArchive.Search.Tests;

public class OoxmlTextExtractorTests
{
    [Fact]
    public void Docx_ParagraphText_IsExtracted()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>Home insurance policy</w:t></w:r></w:p>
                <w:p><w:r><w:t>Covers fire and water damage.</w:t></w:r></w:p>
              </w:body>
            </w:document>
            """;
        using var stream = BuildZip(("word/document.xml", documentXml));

        string? text = DocumentTextExtractor.Extract("policy.docx", stream);

        Assert.NotNull(text);
        Assert.Contains("Home insurance policy", text);
        Assert.Contains("Covers fire and water damage.", text);
    }

    [Fact]
    public void Xlsx_SharedStrings_AreExtracted()
    {
        const string sharedStrings = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2">
              <si><t>Account number</t></si>
              <si><t>DE89 3704 0044</t></si>
            </sst>
            """;
        using var stream = BuildZip(("xl/sharedStrings.xml", sharedStrings));

        string? text = DocumentTextExtractor.Extract("accounts.xlsx", stream);

        Assert.NotNull(text);
        Assert.Contains("Account number", text);
        Assert.Contains("DE89 3704 0044", text);
    }

    [Fact]
    public void Pptx_SlideText_IsExtracted()
    {
        const string slide1 = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:cSld><p:spTree><a:t>Emergency contacts</a:t></p:spTree></p:cSld>
            </p:sld>
            """;
        const string slide2 = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:cSld><p:spTree><a:t>Fire brigade 112</a:t></p:spTree></p:cSld>
            </p:sld>
            """;
        using var stream = BuildZip(("ppt/slides/slide1.xml", slide1), ("ppt/slides/slide2.xml", slide2));

        string? text = DocumentTextExtractor.Extract("contacts.pptx", stream);

        Assert.NotNull(text);
        Assert.Contains("Emergency contacts", text);
        Assert.Contains("Fire brigade 112", text);
    }

    [Fact]
    public void GarbageDocx_ReturnsNullInsteadOfThrowing()
    {
        using var stream = new MemoryStream("this is not a zip archive"u8.ToArray());

        Assert.Null(DocumentTextExtractor.Extract("broken.docx", stream));
    }

    private static MemoryStream BuildZip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
