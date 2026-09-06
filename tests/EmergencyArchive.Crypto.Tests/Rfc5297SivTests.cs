using EmergencyArchive.Crypto.Vault;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

/// <summary>Conformance tests against RFC 5297 appendix A.1 (AEAD_AES_SIV_CMAC_256).</summary>
public class Rfc5297SivTests
{
    private static readonly byte[] Key =
    [
        0xff, 0xfe, 0xfd, 0xfc, 0xfb, 0xfa, 0xf9, 0xf8,
        0xf7, 0xf6, 0xf5, 0xf4, 0xf3, 0xf2, 0xf1, 0xf0,
        0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7,
        0xf8, 0xf9, 0xfa, 0xfb, 0xfc, 0xfd, 0xfe, 0xff,
    ];

    private static readonly byte[] AssociatedData =
    [
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f,
        0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
    ];

    private static readonly byte[] Plaintext = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee];

    private static readonly byte[] ExpectedOutput =
    [
        0x85, 0x63, 0x2d, 0x07, 0xc6, 0xe8, 0xf3, 0x7f,
        0x95, 0x0a, 0xcd, 0x32, 0x0a, 0x2e, 0xcc, 0x93,
        0x40, 0xc0, 0x2b, 0x96, 0x90, 0xc4, 0xdc, 0x04,
        0xda, 0xef, 0x7f, 0x6a, 0xfe, 0x5c,
    ];

    [Fact]
    public void Ctr_Direct_MatchesRfcA1Intermediates()
    {
        // RFC 5297 A.1: with CTR = V (bits 63/31 cleared) and K2, the keystream
        // and ciphertext must match the RFC's intermediate values exactly.
        byte[] keystream = Rfc5297Siv.Ctr(
            Convert.FromHexString("F0F1F2F3F4F5F6F7F8F9FAFBFCFDFEFF"),
            Convert.FromHexString("85632D07C6E8F37F150ACD320A2ECC93"),
            Convert.FromHexString("112233445566778899AABBCCDDEE"));

        Assert.Equal("40C02B9690C4DC04DAEF7F6AFE5C", Convert.ToHexString(keystream));
    }

    [Fact]
    public void Encrypt_MatchesRfc5297A1Vector()
    {
        byte[] output = Rfc5297Siv.Encrypt(Key, Plaintext, AssociatedData);
        Assert.Equal(ExpectedOutput, output);
    }

    [Fact]
    public void TryDecrypt_MatchesRfc5297A1Vector()
    {
        Assert.True(Rfc5297Siv.TryDecrypt(Key, ExpectedOutput, out byte[] plaintext, AssociatedData));
        Assert.Equal(Plaintext, plaintext);
    }

    [Fact]
    public void TryDecrypt_FailsWithWrongAssociatedData()
    {
        byte[] wrongAd = [.. AssociatedData];
        wrongAd[0] ^= 0x01;

        Assert.False(Rfc5297Siv.TryDecrypt(Key, ExpectedOutput, out byte[] plaintext, wrongAd));
        Assert.Empty(plaintext);
    }

    [Fact]
    public void TryDecrypt_FailsWithTamperedCiphertext()
    {
        byte[] tampered = [.. ExpectedOutput];
        tampered[^1] ^= 0x01;

        Assert.False(Rfc5297Siv.TryDecrypt(Key, tampered, out _, AssociatedData));
    }

    [Fact]
    public void TryDecrypt_FailsWithWrongKey()
    {
        byte[] wrongKey = [.. Key];
        wrongKey[0] ^= 0x01;

        Assert.False(Rfc5297Siv.TryDecrypt(wrongKey, ExpectedOutput, out _, AssociatedData));
    }

    [Fact]
    public void Encrypt_IsDeterministic()
    {
        byte[] first = Rfc5297Siv.Encrypt(Key, Plaintext, AssociatedData);
        byte[] second = Rfc5297Siv.Encrypt(Key, Plaintext, AssociatedData);
        Assert.Equal(first, second);
    }

    [Fact]
    public void RoundTripsEmptyPlaintext()
    {
        byte[] ciphertext = Rfc5297Siv.Encrypt(Key, ReadOnlySpan<byte>.Empty, AssociatedData);
        Assert.Equal(16, ciphertext.Length); // IV only
        Assert.True(Rfc5297Siv.TryDecrypt(Key, ciphertext, out byte[] plaintext, AssociatedData));
        Assert.Empty(plaintext);
    }
}
