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
    /// <summary>
    /// Cap on decompressed bytes read per matched entry. OOXML text parts are
    /// small in practice; this bounds a malicious "zip bomb" DOCX/XLSX/PPTX
    /// whose part decompresses to gigabytes (which the text-length cap alone
    /// would not stop, e.g. megabytes of markup that never yields <t> text).
    /// </summary>
    private const long MaxDecompressedBytesPerEntry = 64L * 1024 * 1024;

    /// <summary>Total decompressed budget across all matched entries.</summary>
    private const long MaxDecompressedBytesTotal = 128L * 1024 * 1024;

    public static string? Extract(Stream content, Func<string, bool> entryFilter, string textElement, string paragraphElement, int maxCharacters)
    {
        using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var sb = new StringBuilder();
        long totalDecompressed = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!entryFilter(entry.FullName))
            {
                continue;
            }

            long remainingTotal = MaxDecompressedBytesTotal - totalDecompressed;
            if (remainingTotal <= 0)
            {
                break;
            }

            long entryBudget = Math.Min(MaxDecompressedBytesPerEntry, remainingTotal);
            using Stream rawEntryStream = entry.Open();
            using var entryStream = new BoundedStream(rawEntryStream, entryBudget);
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = 0, // per-entry byte cap below is the real bound
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

            totalDecompressed += entryStream.BytesRead;
        }

        var text = sb.ToString().Trim();
        return text.Length == 0 ? null : text[..Math.Min(text.Length, maxCharacters)];
    }

    /// <summary>
    /// Read-only pass-through that throws once more than <c>limit</c> bytes have
    /// been read, bounding decompression of a hostile zip entry. The caller's
    /// extraction is wrapped in a catch-all, so hitting the limit safely
    /// degrades to "index this document by name only".
    /// </summary>
    private sealed class BoundedStream(Stream inner, long limit) : Stream
    {
        private long read;

        public long BytesRead => read;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            read += n;
            if (read > limit)
            {
                throw new InvalidDataException("OOXML entry exceeded the decompression size limit.");
            }

            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            int n = inner.Read(buffer);
            read += n;
            if (read > limit)
            {
                throw new InvalidDataException("OOXML entry exceeded the decompression size limit.");
            }

            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
