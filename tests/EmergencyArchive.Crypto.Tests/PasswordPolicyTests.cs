using EmergencyArchive.Crypto;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

public class PasswordPolicyTests
{
    [Fact]
    public void Validate_AcceptsTwelveCharacterPassphrase()
    {
        // Composition rules are deliberately absent (NIST SP 800-63B style):
        // a long passphrase without digits or symbols must pass.
        Assert.Empty(PasswordPolicy.Validate("correct horse battery staple"));
    }

    [Fact]
    public void Validate_AcceptsExactlyMinimumLength()
    {
        Assert.Empty(PasswordPolicy.Validate(new string('x', PasswordPolicy.MinLength)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_RejectsEmptyPassword(string? password)
    {
        Assert.NotEmpty(PasswordPolicy.Validate(password));
    }

    [Fact]
    public void Validate_RejectsTooShortPassword()
    {
        IReadOnlyList<string> errors = PasswordPolicy.Validate("short");

        string errorText = Assert.Single(errors);
        Assert.Contains(PasswordPolicy.MinLength.ToString(), errorText);
    }

    [Fact]
    public void Validate_RejectsOverlongPassword()
    {
        IReadOnlyList<string> errors = PasswordPolicy.Validate(new string('x', PasswordPolicy.MaxLength + 1));

        Assert.Contains(errors, e => e.Contains(PasswordPolicy.MaxLength.ToString()));
    }
}
