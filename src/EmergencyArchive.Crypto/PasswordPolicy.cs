namespace EmergencyArchive.Crypto;

/// <summary>
/// Password rules enforced by the application before a vault is created or a
/// password is changed (spec sections 5 and 19).
/// </summary>
/// <remarks>
/// Composition rules are deliberately absent (NIST SP 800-63B style): length is
/// the primary strength factor because an attacker holding the USB can mount an
/// offline guessing attack, so a long memorable passphrase beats a short
/// symbol-laden password. The vault's memory-hard KDF (Argon2id preferred, spec
/// section 5) is the other half of the offline-attack defense.
/// </remarks>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 256;

    /// <summary>Returns the list of policy violations; an empty list means the password is acceptable.</summary>
    public static IReadOnlyList<string> Validate(string? password)
    {
        List<string> errors = [];

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Enter a password.");
            return errors;
        }

        if (password.Length < MinLength)
        {
            errors.Add(
                $"The password must be at least {MinLength} characters long. " +
                "A passphrase of four or five random words works well.");
        }

        if (password.Length > MaxLength)
        {
            errors.Add($"The password must be at most {MaxLength} characters long.");
        }

        return errors;
    }
}
