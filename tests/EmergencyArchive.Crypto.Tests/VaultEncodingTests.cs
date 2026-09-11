using System.Text;
using EmergencyArchive.Crypto.Vault;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

public class VaultEncodingTests
{
    // RFC 4648 section 10 test vectors (base64), transformed to base64url.
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg==")]
    [InlineData("fo", "Zm8=")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg==")]
    [InlineData("fooba", "Zm9vYmE=")]
    [InlineData("foobar", "Zm9vYmFy")]
    public void EncodeBase64Url_KeepsPadding(string input, string expected)
    {
        Assert.Equal(expected, VaultEncoding.EncodeBase64Url(Encoding.UTF8.GetBytes(input)));
        Assert.Equal(input, Encoding.UTF8.GetString(VaultEncoding.DecodeBase64Url(expected)));
    }

    [Fact]
    public void EncodeBase64Url_IsUrlSafe()
    {
        // Bytes whose standard base64 contains '+' and '/'.
        byte[] bytes = [0xFB, 0xFF];

        string urlForm = VaultEncoding.EncodeBase64Url(bytes);

        Assert.Equal("-_8=", urlForm);
        Assert.Equal(bytes, VaultEncoding.DecodeBase64Url(urlForm));
    }

    [Fact]
    public void EncodeJwtSegment_OmitsPadding()
    {
        Assert.Equal("Zm8", VaultEncoding.EncodeJwtSegment("fo"u8.ToArray()));
        Assert.Equal("fo", Encoding.UTF8.GetString(VaultEncoding.DecodeJwtSegment("Zm8")));
    }

    // RFC 4648 section 10 test vectors (base32).
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foob", "MZXW6YQ=")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void EncodeBase32_MatchesRfc4648Vectors(string input, string expected)
    {
        Assert.Equal(expected, VaultEncoding.EncodeBase32(Encoding.UTF8.GetBytes(input)));
    }

    [Fact]
    public void EncodeBase32_OfSha1Length_HasNoPadding()
    {
        // 20-byte SHA-1 digests encode to exactly 32 Base32 characters.
        byte[] digest = new byte[20];

        string encoded = VaultEncoding.EncodeBase32(digest);

        Assert.Equal(32, encoded.Length);
        Assert.DoesNotContain('=', encoded);
    }
}
