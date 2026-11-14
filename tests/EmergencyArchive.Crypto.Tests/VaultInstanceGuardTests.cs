using EmergencyArchive.Crypto.Vault;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

/// <summary>
/// Red/blue cycle (2026-11): single-instance guard. Opening an archive that is
/// already open in another instance is refused with VaultInUseException (the
/// write gate is per session, so two instances could otherwise interleave
/// manifest/index writes that only VERIFY would catch afterwards). The lock
/// file lives at the DRIVE root — never inside vault\ — so Cryptomator interop
/// is unaffected, and it is released (and removed) when the session closes.
/// </summary>
public sealed class VaultInstanceGuardTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string driveRoot;
    private readonly string vaultDir;

    public VaultInstanceGuardTests()
    {
        driveRoot = Path.Combine(Path.GetTempPath(), $"ea-guard-{Guid.NewGuid():N}");
        vaultDir = Path.Combine(driveRoot, "vault");
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(driveRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private static bool sessionIsUsable(VaultSession session)
    {
        session.WriteFile("guard-probe.txt", new MemoryStream([1, 2, 3]));
        return session.FileExists("guard-probe.txt");
    }

    [Fact]
    public void SecondUnlock_WhileTheFirstSessionIsOpen_IsRefused()
    {
        using VaultSession first = VaultStore.Unlock(vaultDir, Password);

        var second = Assert.Throws<VaultInUseException>(() => VaultStore.Unlock(vaultDir, Password));
        Assert.Contains("already open", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterTheFirstSessionCloses_TheArchiveOpensAgain()
    {
        using (VaultSession first = VaultStore.Unlock(vaultDir, Password))
        {
            Assert.True(sessionIsUsable(first));
        }

        using VaultSession second = VaultStore.Unlock(vaultDir, Password);
        Assert.True(sessionIsUsable(second));
    }

    [Fact]
    public void LockFile_LivesInsideTheVault_PerVault_NotAtTheSharedParent()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);

        // Per-vault placement: a lock at the shared parent (e.g. /tmp) would
        // collide across unrelated vaults opened in parallel.
        Assert.True(File.Exists(Path.Combine(vaultDir, ".emergency-archive-lock")));
        Assert.False(File.Exists(Path.Combine(driveRoot, ".emergency-archive-lock")));

        // It is infrastructure, not a document: never listed by the app.
        Assert.DoesNotContain(session.EnumerateFiles(), p => p.Contains("lock", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClosingTheSession_RemovesTheLockFile()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            Assert.True(File.Exists(Path.Combine(vaultDir, ".emergency-archive-lock")));
        }

        Assert.False(File.Exists(Path.Combine(vaultDir, ".emergency-archive-lock")));
    }
}
