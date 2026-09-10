using EmergencyArchive.UI;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Guards spec section 19/21: locking must not leave the password (even
/// obfuscated) sitting in any entry box.
/// </summary>
public class LockClearsPasswordTests
{
    [Fact]
    public void Lock_ClearsThePasswordEntry()
    {
        var vm = new MainWindowViewModel { Password = "correct horse battery staple" };

        vm.Lock();

        Assert.True(string.IsNullOrEmpty(vm.Password));
    }

    [Fact]
    public void Lock_ClearsTheCreateArchivePasswordEntries()
    {
        var vm = new MainWindowViewModel
        {
            NewArchivePassword = "a long passphrase here",
            ConfirmArchivePassword = "a long passphrase here",
        };

        vm.Lock();

        Assert.True(string.IsNullOrEmpty(vm.NewArchivePassword));
        Assert.True(string.IsNullOrEmpty(vm.ConfirmArchivePassword));
    }
}
