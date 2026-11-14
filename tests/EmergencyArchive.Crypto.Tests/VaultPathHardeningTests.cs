using EmergencyArchive.Crypto.Vault;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

/// <summary>
/// Red/blue cycle (2026-11): lock in the vault's path-traversal defenses with
/// adversarial tests. Cleartext vault paths are split into segments and every
/// segment is SIV-encrypted into a flattened ciphertext directory, so `..`,
/// absolute paths, and drive letters can never reach outside the vault — but
/// the behavior must stay guaranteed, not assumed.
/// </summary>
public sealed class VaultPathHardeningTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string vaultDir = Path.Combine(
        Path.GetTempPath(), $"ea-traversal-{Guid.NewGuid():N}");

    public VaultPathHardeningTests()
    {
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(vaultDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../b.txt")]
    [InlineData("..")]
    [InlineData("a/../b")]
    [InlineData("")]
    [InlineData("   ")]
    public void Vault_Paths_RejectTraversalAndEmptyPaths(string hostilePath)
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);

        Assert.Throws<ArgumentException>(() => session.WriteFile(hostilePath, new MemoryStream([1, 2, 3])));
        Assert.Throws<ArgumentException>(() => session.ReadFile(hostilePath, new MemoryStream()));
        Assert.Throws<ArgumentException>(() => session.FileExists(hostilePath));
    }

    [Fact]
    public void Vault_AbsolutePaths_AreTreatedAsPlainSegments_NeverAsHostPaths()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);

        // "C:/evil.txt" splits into segments "C:" + "evil.txt": a folder named
        // "C:" INSIDE the encrypted vault — never a host path.
        session.WriteFile("C:/evil.txt", new MemoryStream([1, 2, 3]));

        Assert.True(session.FileExists("C:/evil.txt"));
        Assert.Equal("C:", session.List().Single(e => e.Kind == VaultEntryKind.Directory).Name);
    }

    [Fact]
    public void Vault_PlaintextNames_NeverReachTheDisk()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);
        byte[] content = "top secret plans"u8.ToArray();
        session.WriteFile("Insurance/policy-XJ901.txt", new MemoryStream(content));

        // Walk every raw file under the vault root: neither the cleartext name
        // nor the cleartext content may appear anywhere on disk.
        foreach (string file in Directory.EnumerateFiles(vaultDir, "*", new EnumerationOptions
                 {
                     RecurseSubdirectories = true,
                     IgnoreInaccessible = true,
                 }))
        {
            byte[] bytes = File.ReadAllBytes(file);
            Assert.DoesNotContain("policy-XJ901.txt"u8, bytes);
            Assert.DoesNotContain(content, bytes);
        }
    }
}
