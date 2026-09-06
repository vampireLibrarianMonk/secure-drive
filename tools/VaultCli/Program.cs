using System.Text;
using EmergencyArchive.Crypto.Vault;

// Interim command-line tool (until Setup Mode, Phase 4):
//   VaultCli create <vault-directory>
//   VaultCli list   <vault-directory>
//   VaultCli put    <vault-directory> <relative-path> <source-file>
//   VaultCli get    <vault-directory> <relative-path> <destination-file>
// Passwords are prompted (never passed via command line, spec section 19).

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "create" => DoCreate(args),
        "list" => DoList(args),
        "put" => DoPut(args),
        "get" => DoGet(args),
        _ => UnknownCommand(args[0]),
    };
}
catch (VaultUnlockException)
{
    Console.Error.WriteLine("Unable to unlock archive. The password was wrong.");
    return 2;
}
catch (VaultException e)
{
    Console.Error.WriteLine($"Archive error: {e.Message}");
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  VaultCli create <vault-directory>");
    Console.WriteLine("  VaultCli list   <vault-directory>");
    Console.WriteLine("  VaultCli put    <vault-directory> <relative-path> <source-file>");
    Console.WriteLine("  VaultCli get    <vault-directory> <relative-path> <destination-file>");
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 1;
}

static int DoCreate(string[] args)
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Usage: VaultCli create <vault-directory>");
        return 1;
    }

    string password = PromptPassword("Archive password: ");
    string confirm = PromptPassword("Repeat password:    ");
    if (password != confirm)
    {
        Console.Error.WriteLine("The two passwords do not match.");
        return 1;
    }

    VaultStore.Create(args[1], password);
    Console.WriteLine($"Vault created at {Path.GetFullPath(args[1])}.");
    return 0;
}

static int DoList(string[] args)
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Usage: VaultCli list <vault-directory>");
        return 1;
    }

    using VaultSession session = VaultStore.Unlock(args[1], PromptPassword("Archive password: "));
    foreach (string path in session.EnumerateFiles())
    {
        Console.WriteLine(path);
    }

    Console.WriteLine($"{session.VaultId}: listed.");
    return 0;
}

static int DoPut(string[] args)
{
    if (args.Length != 4)
    {
        Console.Error.WriteLine("Usage: VaultCli put <vault-directory> <relative-path> <source-file>");
        return 1;
    }

    using VaultSession session = VaultStore.Unlock(args[1], PromptPassword("Archive password: "));
    using FileStream source = File.OpenRead(args[3]);
    session.WriteFile(args[2], source);
    Console.WriteLine($"Stored {args[2]}.");
    return 0;
}

static int DoGet(string[] args)
{
    if (args.Length != 4)
    {
        Console.Error.WriteLine("Usage: VaultCli get <vault-directory> <relative-path> <destination-file>");
        return 1;
    }

    using VaultSession session = VaultStore.Unlock(args[1], PromptPassword("Archive password: "));
    File.WriteAllBytes(args[3], session.ReadFile(args[2]));
    Console.WriteLine($"Extracted {args[2]} -> {args[3]}.");
    return 0;
}

static string PromptPassword(string label)
{
    Console.Write(label);
    var password = new StringBuilder();
    while (true)
    {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            break;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Length--;
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
            Console.Write('*');
        }
    }

    return password.ToString();
}
