using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;

namespace EmergencyArchive.UI;

/// <summary>Password change (spec section 12).</summary>
public sealed partial class SetupViewModel
{
    [RelayCommand(CanExecute = nameof(CanChangePassword))]
    private async Task ChangePasswordAsync()
    {
        string newPassword = NewPassword ?? string.Empty;
        if (newPassword != ConfirmNewPassword)
        {
            SetupStatus = "The two passwords do not match.";
            return;
        }

        IReadOnlyList<string> policyErrors = PasswordPolicy.Validate(newPassword);
        if (policyErrors.Count > 0)
        {
            SetupStatus = string.Join(" ", policyErrors);
            return;
        }

        IsBusy = true;
        SetupStatus = "Changing password…";
        try
        {
            await Task.Run(() => session.ChangePassword(newPassword));
            NewPassword = null;
            ConfirmNewPassword = null;
            SetupStatus = "Password changed. Remember to change it on every replica, and update stored copies of the password.";
        }
        catch (ArgumentException e)
        {
            SetupStatus = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
