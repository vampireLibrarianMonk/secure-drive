using System.Text;
using EmergencyArchive.Search.TextExtraction;
using Xunit;

namespace EmergencyArchive.Search.Tests;

public class DocumentTextExtractorTests
{
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("README.md")]
    [InlineData("notes.markdown")]
    [InlineData("data.csv")]
    public void PlainTextFiles_AreExtracted(string fileName)
    {
        using var stream = new MemoryStream("The home insurance policy covers fire damage."u8.ToArray());

        string? text = DocumentTextExtractor.Extract(fileName, stream);

        Assert.NotNull(text);
        Assert.Contains("home insurance policy", text);
    }

    [Fact]
    public void PlainText_Latin1Fallback_HandlesLegacyEncoding()
    {
        // 0xE9 is 'é' in Latin-1 but an invalid standalone UTF-8 byte.
        using var stream = new MemoryStream([0x43, 0x61, 0x66, 0xE9]);

        string? text = DocumentTextExtractor.Extract("cafe.txt", stream);

        Assert.NotNull(text);
        Assert.Contains("Café", text);
    }

    [Fact]
    public void Html_StripsTagsScriptsAndStyles_AndDecodesEntities()
    {
        const string html = """
            <html>
              <head><style>body { color: red; }</style></head>
              <body>
                <h1>Home Insurance</h1>
                <script>alert('evil')</script>
                <p>Coverage &amp; deductibles &lt;explained&gt;</p>
              </body>
            </html>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        string? text = DocumentTextExtractor.Extract("policy.html", stream);

        Assert.NotNull(text);
        Assert.Contains("Home Insurance", text);
        // Entities are decoded AFTER tag stripping, so the decoded angle
        // brackets of inline code samples remain as literal text.
        Assert.Contains("Coverage & deductibles <explained>", text);
        Assert.DoesNotContain("evil", text);
        Assert.DoesNotContain("color: red", text);
    }

    [Fact]
    public void Pdf_Text_IsExtracted()
    {
        using var stream = new MemoryStream(BuildMinimalPdf("Home insurance policy 2026"));

        string? text = DocumentTextExtractor.Extract("policy.pdf", stream);

        Assert.NotNull(text);
        Assert.Contains("Home insurance policy 2026", text);
    }

    [Fact]
    public void UnknownBinaryExtension_ReturnsNull()
    {
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0xFE, 0xFF]);

        Assert.Null(DocumentTextExtractor.Extract("database.bin", stream));
    }

    [Fact]
    public void Docx_DecompressionBomb_DegradesSafely_WithoutExhaustingMemory()
    {
        // Finding 3.1: a DOCX whose word/document.xml decompresses to ~512 MB
        // of highly compressible markup that yields NO <t> text. The text-length
        // cap alone never trips, so the per-entry decompression cap must stop it;
        // extraction then degrades to null (index-by-name-only) rather than
        // reading the whole bomb into memory.
        byte[] bomb = BuildOoxmlBomb("word/document.xml", 512L * 1024 * 1024);

        using var stream = new MemoryStream(bomb);
        string? text = DocumentTextExtractor.Extract("bomb.docx", stream);

        Assert.Null(text); // stopped by the decompression cap, safely
    }

    /// <summary>Builds a zip with one entry of <paramref name="uncompressedBytes"/> of a repeated, highly-compressible byte (no OOXML text elements).</summary>
    private static byte[] BuildOoxmlBomb(string entryName, long uncompressedBytes)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            System.IO.Compression.ZipArchiveEntry entry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.SmallestSize);
            using Stream s = entry.Open();
            byte[] block = new byte[64 * 1024];
            Array.Fill(block, (byte)' '); // whitespace: valid-ish XML filler, no <t>
            long written = 0;
            while (written < uncompressedBytes)
            {
                int n = (int)Math.Min(block.Length, uncompressedBytes - written);
                s.Write(block, 0, n);
                written += n;
            }
        }

        return ms.ToArray();
    }

    [Fact]
    public void Extraction_IsCapped()
    {
        byte[] large = new byte[DocumentTextExtractor.DefaultMaxCharacters + 50_000];
        for (int i = 0; i < large.Length; i++)
        {
            large[i] = (byte)('a' + (i % 26));
        }

        using var stream = new MemoryStream(large);

        string? text = DocumentTextExtractor.Extract("huge.txt", stream);

        Assert.NotNull(text);
        Assert.True(text.Length <= DocumentTextExtractor.DefaultMaxCharacters);
    }

    /// <summary>
    /// Builds a minimal but structurally valid single-page PDF with an
    /// uncompressed content stream, tracking xref offsets exactly.
    /// </summary>
    private static byte[] BuildMinimalPdf(string text)
    {
        using var ms = new MemoryStream();
        var offsets = new List<long>();

        void Write(string value) => ms.Write(Encoding.Latin1.GetBytes(value));
        void WriteObject(string body)
        {
            offsets.Add(ms.Position);
            Write(body);
        }

        Write("%PDF-1.4\n");
        WriteObject("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        WriteObject("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        WriteObject("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");

        string streamContent = $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";
        WriteObject($"4 0 obj\n<< /Length {streamContent.Length} >>\nstream\n{streamContent}\nendstream\nendobj\n");
        WriteObject("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        long xrefPosition = ms.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            Write(offset.ToString("D10") + " 00000 n \n");
        }

        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + xrefPosition + "\n%%EOF");

        return ms.ToArray();
    }
}
