using System.Xml;
using System.Xml.Linq;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// Parses and builds the KDBX inner XML payload. Protected values (password and
/// any field with <c>Protected="True"</c>) are base64-encoded ciphertext from
/// the inner random stream; the stream is consumed in document order, so this
/// walks every &lt;Value&gt; element in order and pulls from the stream only
/// for protected ones.
/// </summary>
internal static class KdbxXml
{
    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit, // no external entities / XXE
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>
    /// Parses the decrypted, decompressed XML into a flat entry list.
    /// <paramref name="unprotect"/> transforms one protected value's ciphertext
    /// into plaintext bytes (it must be called for every protected value in
    /// document order to keep the inner stream in sync).
    /// </summary>
    public static KdbxDatabase Parse(byte[] xmlBytes, Func<byte[], byte[]> unprotect)
    {
        XDocument doc;
        using (var ms = new MemoryStream(xmlBytes))
        using (XmlReader xr = XmlReader.Create(ms, SafeReaderSettings))
        {
            try
            {
                doc = XDocument.Load(xr);
            }
            catch (XmlException e)
            {
                throw new KdbxFormatException("KDBX payload is not valid XML.", e);
            }
        }

        // First pass: decrypt every protected value in document order so the
        // inner stream is consumed correctly, storing the plaintext on the node.
        foreach (XElement value in doc.Descendants("Value"))
        {
            if (IsProtected(value))
            {
                string b64 = value.Value;
                byte[] plain = string.IsNullOrEmpty(b64)
                    ? []
                    : unprotect(Convert.FromBase64String(b64));
                value.Value = System.Text.Encoding.UTF8.GetString(plain);
            }
        }

        var database = new KdbxDatabase();
        XElement? root = doc.Root?.Element("Root");
        if (root is null)
        {
            return database;
        }

        foreach (XElement entry in root.Descendants("Entry"))
        {
            // Skip entries inside the Recycle Bin is not tracked here; KeePass
            // marks history entries under <History>, which we exclude.
            if (entry.Ancestors("History").Any())
            {
                continue;
            }

            database.Entries.Add(ReadEntry(entry));
        }

        return database;
    }

    private static KdbxEntry ReadEntry(XElement entry)
    {
        var result = new KdbxEntry();
        foreach (XElement str in entry.Elements("String"))
        {
            string key = str.Element("Key")?.Value ?? string.Empty;
            string val = str.Element("Value")?.Value ?? string.Empty;
            switch (key)
            {
                case "Title": result.Title = val; break;
                case "UserName": result.UserName = val; break;
                case "Password": result.Password = val; break;
                case "URL": result.Url = val; break;
                case "Notes": result.Notes = val; break;
                default:
                    if (!string.IsNullOrEmpty(key))
                    {
                        result.CustomFields.Add(new KeyValuePair<string, string>(key, val));
                    }

                    break;
            }
        }

        return result;
    }

    private static bool IsProtected(XElement value)
    {
        string? attr = value.Attribute("Protected")?.Value;
        return string.Equals(attr, "True", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the KDBX inner XML from a database. <paramref name="protect"/>
    /// transforms a protected plaintext value's bytes into ciphertext; it is
    /// called for each protected value in document order. The password field is
    /// marked Protected; other fields are stored in the clear (within the
    /// already-encrypted database), matching KeePass defaults.
    /// </summary>
    public static byte[] Build(KdbxDatabase database, Func<byte[], byte[]> protect, DateTime nowUtc)
    {
        string time = nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var root = new XElement("Root");
        var group = new XElement("Group",
            new XElement("UUID", Convert.ToBase64String(Guid.NewGuid().ToByteArray())),
            new XElement("Name", "Imported"));

        foreach (KdbxEntry entry in database.Entries)
        {
            group.Add(BuildEntry(entry, protect, time));
        }

        root.Add(group);

        var meta = new XElement("Meta",
            new XElement("Generator", "EmergencyArchive"),
            new XElement("DatabaseName", "Emergency Archive Export"),
            new XElement("RecycleBinEnabled", "False"));

        var keePassFile = new XElement("KeePassFile", meta, root);
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), keePassFile);

        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), Indent = false };
        using (XmlWriter xw = XmlWriter.Create(ms, settings))
        {
            doc.Save(xw);
        }

        return ms.ToArray();
    }

    private static XElement BuildEntry(KdbxEntry entry, Func<byte[], byte[]> protect, string time)
    {
        var el = new XElement("Entry",
            new XElement("UUID", Convert.ToBase64String(Guid.NewGuid().ToByteArray())),
            new XElement("Times",
                new XElement("CreationTime", time),
                new XElement("LastModificationTime", time),
                new XElement("Expires", "False")));

        el.Add(PlainString("Title", entry.Title));
        el.Add(PlainString("UserName", entry.UserName));
        el.Add(ProtectedString("Password", entry.Password, protect));
        el.Add(PlainString("URL", entry.Url));
        el.Add(PlainString("Notes", entry.Notes));

        foreach (KeyValuePair<string, string> field in entry.CustomFields)
        {
            el.Add(PlainString(field.Key, field.Value));
        }

        return el;
    }

    private static XElement PlainString(string key, string value) =>
        new("String", new XElement("Key", key), new XElement("Value", value ?? string.Empty));

    private static XElement ProtectedString(string key, string value, Func<byte[], byte[]> protect)
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
        byte[] cipher = protect(plain);
        return new XElement("String",
            new XElement("Key", key),
            new XElement("Value",
                new XAttribute("Protected", "True"),
                Convert.ToBase64String(cipher)));
    }
}
