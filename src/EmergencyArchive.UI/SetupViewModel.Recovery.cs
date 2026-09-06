using System.IO;
using System.Text;

namespace EmergencyArchive.UI;

/// <summary>Recovery-instructions content (keep in sync with scripts/new-usb.ps1).</summary>
public sealed partial class SetupViewModel
{
    private static string BuildRecoveryInstructions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("EMERGENCY ARCHIVE - RECOVERY INSTRUCTIONS");
        sb.AppendLine("=========================================");
        sb.AppendLine();
        sb.AppendLine("PURPOSE");
        sb.AppendLine("  The documents on this drive are stored in a Cryptomator vault");
        sb.AppendLine("  (format 8, cipher combination SIV_GCM) - a publicly documented,");
        sb.AppendLine("  open-source encryption format:");
        sb.AppendLine("    - https://docs.cryptomator.org (Security section)");
        sb.AppendLine("    - https://github.com/cryptomator/cryptolib");
        sb.AppendLine();
        sb.AppendLine("  Unlocking requires exactly one secret: the archive password.");
        sb.AppendLine("  There is no other key on this drive.");
        sb.AppendLine();
        sb.AppendLine("WHERE THE ENCRYPTED DATA LIVES");
        sb.AppendLine("  vault\\masterkey.cryptomator   password-based key material");
        sb.AppendLine("  vault\\vault.cryptomator       signed vault configuration");
        sb.AppendLine("  vault\\d\\                      the encrypted documents");
        sb.AppendLine();
        sb.AppendLine("RECOVERY PROCEDURE (RECOMMENDED)");
        sb.AppendLine("  1. Install the free, open-source Cryptomator application:");
        sb.AppendLine("     https://cryptomator.org");
        sb.AppendLine("  2. Plug in this USB drive.");
        sb.AppendLine("  3. Add Vault -> Open existing vault -> select");
        sb.AppendLine("     vault\\masterkey.cryptomator on this drive.");
        sb.AppendLine("  4. Enter the archive password. The vault opens as a drive.");
        sb.AppendLine("  5. Copy the documents you need. Lock the vault afterwards.");
        sb.AppendLine();
        sb.AppendLine("RECOVERY PROCEDURE (WITHOUT INSTALLING SOFTWARE)");
        sb.AppendLine("  The format is fully documented (links above): the KEK is");
        sb.AppendLine("  scrypt(password, salt, N, r, p=1, 32 bytes); the two AES-256");
        sb.AppendLine("  masterkeys are AES-KW wrapped; names and contents are protected");
        sb.AppendLine("  with AES-SIV and AES-GCM as specified in the format docs.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT RULES");
        sb.AppendLine("  - Do not save, move, rename, or delete any file inside 'vault\\'.");
        sb.AppendLine("  - If integrity problems occur, stop writing to this drive and");
        sb.AppendLine("    check whether another replica exists.");
        return sb.ToString();
    }
}
