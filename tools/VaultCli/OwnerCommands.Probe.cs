using System.IO;
using System.Text;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Search.TextExtraction;
using EmergencyArchive.Sync;

namespace VaultCli;

/// <summary>Replica inspection and comparison commands (spec section 17).</summary>
internal static partial class OwnerCommands
{
    public static int Probe(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: VaultCli probe <file>");
            return 1;
        }

        string path = args[1];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using FileStream stream = File.OpenRead(path);
        string? text = DocumentTextExtractor.Extract(Path.GetFileName(path), stream);
        sw.Stop();

        if (text is null)
        {
            Console.WriteLine($"EXTRACTION: no text (scanned image, unsupported, or empty) [{sw.ElapsedMilliseconds} ms]");
            return 0;
        }

        int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        Console.WriteLine($"EXTRACTION: {text.Length} chars, ~{words} words [{sw.ElapsedMilliseconds} ms]");
        Console.WriteLine("PREVIEW: " + text[..Math.Min(200, text.Length)].Replace("\n", " / ").Replace("\r", ""));
        return 0;
    }
}
