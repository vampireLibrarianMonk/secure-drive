using EmergencyArchive.Integrity;
using Xunit;

namespace EmergencyArchive.Integrity.Tests;

public class Sha256Tests
{
    [Fact]
    public void ComputeHash_MatchesKnownVectorAbc()
    {
        using MemoryStream stream = new("abc"u8.ToArray());

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Sha256.ComputeHash(stream));
    }

    [Fact]
    public void ComputeHash_MatchesKnownVectorEmpty()
    {
        using MemoryStream stream = new(Array.Empty<byte>());

        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Sha256.ComputeHash(stream));
    }

    [Fact]
    public void ComputeFile_MatchesStreamHash()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emergencyarchive-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(path, "emergency archive integrity"u8.ToArray());

            using FileStream stream = File.OpenRead(path);
            string expected = Sha256.ComputeHash(stream);

            Assert.Equal(expected, Sha256.ComputeFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ComputeHash_ProducesLowercaseHex()
    {
        using MemoryStream stream = new("abc"u8.ToArray());

        string hash = Sha256.ComputeHash(stream);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
    }
}
