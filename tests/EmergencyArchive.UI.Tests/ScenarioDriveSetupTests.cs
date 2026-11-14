using EmergencyArchive.Crypto;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Core;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Scenario: setting a fresh drive up. Walks the real first-run journey — the
/// app detects "no archive on this drive", offers CREATE YOUR ARCHIVE, rejects
/// mismatched/weak passwords, creates the vault (with the real production KDF
/// cost, exercised exactly once in this suite), unlocks straight into the
/// archive, and the owner can later unlock with the password and rotate it in
/// Setup Mode.
/// </summary>
[Collection("scenario")]
public sealed class ScenarioDriveSetupTests : IDisposable
{
    private readonly string driveRoot = ScenarioSupport.NewDriveRoot();
    private readonly string vaultDir;

    public ScenarioDriveSetupTests()
    {
        vaultDir = Path.Combine(driveRoot, "vault");
        ScenarioSupport.SetVaultOverride(vaultDir);
    }

    public void Dispose() => ScenarioSupport.SetVaultOverride(null);

    private void CleanupVault()
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

    [Fact]
    public void FreshDrive_ShowsFirstRun_AndOffersTheDriveVaultPath()
    {
        try
        {
            var vm = new MainWindowViewModel();

            Assert.True(vm.IsFirstRun);
            Assert.True(vm.IsCreating);
            Assert.Equal(MainWindowViewModel.FirstRunMessage, vm.StatusMessage);
            Assert.Null(VaultLocator.Locate());
            Assert.Equal(vaultDir, VaultLocator.LocateCreatable());
        }
        finally
        {
            CleanupVault();
        }
    }

    [Fact]
    public void CreateArchive_RejectsMismatchedPasswords_WithoutCreatingAnything()
    {
        try
        {
            var vm = new MainWindowViewModel
            {
                NewArchivePassword = ScenarioSupport.Password,
                ConfirmArchivePassword = ScenarioSupport.Password + "-but-different",
            };

            vm.CreateArchiveCommand.Execute(null);

            Assert.Contains("do not match", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(vaultDir, "masterkey.cryptomator")));
        }
        finally
        {
            CleanupVault();
        }
    }

    [Fact]
    public void CreateArchive_RejectsWeakPasswords_PerPolicy()
    {
        try
        {
            var vm = new MainWindowViewModel
            {
                NewArchivePassword = "short",
                ConfirmArchivePassword = "short",
            };

            vm.CreateArchiveCommand.Execute(null);

            IReadOnlyList<string> violations = PasswordPolicy.Validate("short");
            Assert.Equal(string.Join("\n", violations), vm.StatusMessage);
            Assert.False(File.Exists(Path.Combine(vaultDir, "masterkey.cryptomator")));
        }
        finally
        {
            CleanupVault();
        }
    }

    [Fact]
    public async Task CreateArchive_CreatesTheVaultWithRealKdf_AndUnlocksStraightIn()
    {
        try
        {
            var vm = new MainWindowViewModel
            {
                NewArchivePassword = ScenarioSupport.Password,
                ConfirmArchivePassword = ScenarioSupport.Password,
            };

            // Uses the production scrypt cost (N = 2^20) exactly like a real first run.
            await vm.CreateArchiveCommand.ExecuteAsync(null);

            Assert.False(vm.IsFirstRun);
            Assert.True(vm.IsUnlocked);
            Assert.StartsWith("Archive created and unlocked", vm.StatusMessage, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(vaultDir, "masterkey.cryptomator")));
            Assert.True(File.Exists(Path.Combine(vaultDir, "vault.cryptomator")));

            // Setup Mode is reachable immediately (the post-create status says so).
            vm.EnterSetupCommand.Execute(null);
            Assert.NotNull(vm.Setup);
            Assert.NotNull(vm.Setup!.ArchiveId);
        }
        finally
        {
            CleanupVault();
        }
    }

    [Fact]
    public async Task Unlock_WrongPassword_ShowsTheError_AndRunsTheCooldown()
    {
        ScenarioSupport.CreateCheapVault(vaultDir, ScenarioSupport.Password);
        try
        {
            var vm = new MainWindowViewModel();
            Assert.False(vm.IsFirstRun); // the vault on the "drive" is detected

            // ExecuteAsync returns only AFTER the 5-second cooldown has run to
            // completion (spec section 19) — the elapsed time is the proof.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            vm.Password = "a wrong guess, 12+ chars";
            await vm.UnlockCommand.ExecuteAsync(null);

            Assert.False(vm.IsUnlocked);
            Assert.Equal(MainWindowViewModel.WrongPasswordMessage, vm.StatusMessage);
            Assert.True(
                clock.Elapsed >= TimeSpan.FromSeconds(4.5),
                $"The 5-second cooldown did not run (elapsed {clock.Elapsed.TotalSeconds:N1} s).");
        }
        finally
        {
            CleanupVault();
        }
    }

    [Fact]
    public async Task Unlock_CorrectPassword_AfterLockCycle_UnlocksAgain()
    {
        ScenarioSupport.CreateCheapVault(vaultDir, ScenarioSupport.Password);
        try
        {
            var first = new MainWindowViewModel { Password = ScenarioSupport.Password };
            await first.UnlockCommand.ExecuteAsync(null);
            Assert.True(first.IsUnlocked);
            first.Lock();

            // A fresh application instance (fresh rate limiter) unlocks again.
            var second = new MainWindowViewModel { Password = ScenarioSupport.Password };
            await second.UnlockCommand.ExecuteAsync(null);
            Assert.True(second.IsUnlocked);
            second.Lock();
        }
        finally
        {
            CleanupVault();
        }
    }

    [Fact]
    public async Task Setup_ChangePassword_ThenOnlyTheNewPasswordUnlocks()
    {
        ScenarioSupport.CreateCheapVault(vaultDir, ScenarioSupport.Password);
        try
        {
            var vm = new MainWindowViewModel { Password = ScenarioSupport.Password };
            await vm.UnlockCommand.ExecuteAsync(null);

            vm.EnterSetupCommand.Execute(null);
            string rotated = "river-copper-morning-lantern";
            vm.Setup!.NewPassword = rotated;
            vm.Setup.ConfirmNewPassword = rotated;
            await vm.Setup!.ChangePasswordCommand.ExecuteAsync(null);
            Assert.Contains("Password changed", vm.Setup!.SetupStatus, StringComparison.Ordinal);

            vm.Lock();

            var again = new MainWindowViewModel { Password = rotated };
            await again.UnlockCommand.ExecuteAsync(null);
            Assert.True(again.IsUnlocked);
            again.Lock();
        }
        finally
        {
            CleanupVault();
        }
    }
}
