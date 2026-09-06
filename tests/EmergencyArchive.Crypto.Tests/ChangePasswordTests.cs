using EmergencyArchive.Crypto.Vault;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

public class ChangePasswordTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private const string NewPassword = "river-copper-morning-lantern-2026";
    private readonly string vaultDir;
    private readonly byte[] documentContent = "Home insurance policy contents"u8.ToArray();

    public ChangePasswordTests()
    {
        vaultDir = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        VaultStore.Create(vaultDir, Password, scryptCostParam: 1 << 10, scryptBlockSize: 1);
        using var session = VaultStore.Unlock(vaultDir, Password);
        session.WriteFile("policy.txt", documentContent);
    }

    public void Dispose() => Directory.Delete(vaultDir, recursive: true);

    [Fact]
    public void ChangePassword_OldPasswordStopsWorking_NewPasswordWorks()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.ChangePassword(NewPassword);
        }

        Assert.Throws<VaultUnlockException>(() => VaultStore.Unlock(vaultDir, Password));

        using VaultSession reopened = VaultStore.Unlock(vaultDir, NewPassword);
        Assert.Equal(documentContent, reopened.ReadFile("policy.txt"));
    }

    [Fact]
    public void ChangePassword_RejectsPolicyViolations()
    {
        using VaultSession session = VaultStore.Unlock(vaultDir, Password);

        var exception = Assert.Throws<ArgumentException>(() => session.ChangePassword("short"));

        Assert.Contains("at least 12 characters", exception.Message);
        // The old password still works — nothing was changed.
        using VaultSession unchanged = VaultStore.Unlock(vaultDir, Password);
        Assert.Equal(documentContent, unchanged.ReadFile("policy.txt"));
    }

    [Fact]
    public void ChangePassword_ThenChangeBack_RestoresOriginalAccess()
    {
        using (VaultSession session = VaultStore.Unlock(vaultDir, Password))
        {
            session.ChangePassword(NewPassword);
        }

        using (VaultSession session = VaultStore.Unlock(vaultDir, NewPassword))
        {
            session.ChangePassword(Password);
        }

        using VaultSession restored = VaultStore.Unlock(vaultDir, Password);
        Assert.Equal(documentContent, restored.ReadFile("policy.txt"));
    }
}
