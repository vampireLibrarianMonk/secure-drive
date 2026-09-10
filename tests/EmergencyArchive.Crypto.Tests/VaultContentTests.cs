using EmergencyArchive.Crypto.Vault;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

public class VaultContentTests
{
    private static readonly byte[] TestEncryptionKey = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20];
    private static readonly byte[] TestMacKey = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40];

    private readonly VaultKeys keys = new(TestEncryptionKey, TestMacKey);

    [Fact]
    public void Header_RoundTrips()
    {
        (byte[] header, byte[] contentKey) = VaultContent.EncryptHeader(keys);

        Assert.Equal(VaultContent.HeaderSize, header.Length);
        Assert.Equal(contentKey, VaultContent.DecryptHeader(keys, header));
    }

    [Fact]
    public void DecryptHeader_WithTamperedHeader_Throws()
    {
        (byte[] header, _) = VaultContent.EncryptHeader(keys);
        header[20] ^= 0xFF;

        Assert.Throws<VaultIntegrityException>(() => VaultContent.DecryptHeader(keys, header));
    }

    [Fact]
    public void DecryptHeader_WithWrongKey_Throws()
    {
        (byte[] header, _) = VaultContent.EncryptHeader(keys);
        using var otherKeys = new VaultKeys([0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60], [0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x10]);

        Assert.Throws<VaultIntegrityException>(() => VaultContent.DecryptHeader(otherKeys, header));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(VaultContent.ChunkPayloadSize)]      // exactly one chunk
    [InlineData(VaultContent.ChunkPayloadSize + 1)]  // two chunks
    [InlineData(70 * 1024)]                          // three chunks (a "large PDF")
    public void Content_RoundTrips(int size)
    {
        byte[] plaintext = new byte[size];
        Random.Shared.NextBytes(plaintext);
        using var plainStream = new MemoryStream(plaintext);
        using var cipherStream = new MemoryStream();

        VaultContent.EncryptStream(keys, plainStream, cipherStream);
        cipherStream.Position = 0;
        using var decrypted = new MemoryStream();
        VaultContent.DecryptStream(keys, cipherStream, decrypted);

        Assert.Equal(plaintext, decrypted.ToArray());
    }

    [Fact]
    public void DecryptStream_WithTruncatedFile_Throws()
    {
        using var plainStream = new MemoryStream(new byte[1024]);
        using var cipherStream = new MemoryStream();
        VaultContent.EncryptStream(keys, plainStream, cipherStream);

        byte[] truncated = cipherStream.ToArray()[..(VaultContent.HeaderSize + 10)];
        using var truncatedStream = new MemoryStream(truncated);
        using var target = new MemoryStream();

        Assert.Throws<VaultIntegrityException>(() => VaultContent.DecryptStream(keys, truncatedStream, target));
    }

    [Fact]
    public void DecryptStream_WithFlippedBit_Throws()
    {
        using var plainStream = new MemoryStream(new byte[2048]);
        using var cipherStream = new MemoryStream();
        VaultContent.EncryptStream(keys, plainStream, cipherStream);

        byte[] corrupted = cipherStream.ToArray();
        corrupted[^5] ^= 0x01; // flip a bit inside the last chunk's tag
        using var corruptedStream = new MemoryStream(corrupted);
        using var target = new MemoryStream();

        Assert.Throws<VaultIntegrityException>(() => VaultContent.DecryptStream(keys, corruptedStream, target));
    }

    [Fact]
    public void DecryptStream_WithReorderedChunks_Throws()
    {
        // Two full chunks; swapping them must fail because each chunk's AAD
        // binds its 64-bit chunk index (defends against block reordering).
        byte[] plaintext = new byte[VaultContent.ChunkPayloadSize * 2];
        Random.Shared.NextBytes(plaintext);
        using var plainStream = new MemoryStream(plaintext);
        using var cipherStream = new MemoryStream();
        VaultContent.EncryptStream(keys, plainStream, cipherStream);

        byte[] bytes = cipherStream.ToArray();
        byte[] swapped = new byte[bytes.Length];
        Array.Copy(bytes, 0, swapped, 0, VaultContent.HeaderSize); // header stays
        int c0 = VaultContent.HeaderSize;
        int c1 = VaultContent.HeaderSize + VaultContent.ChunkSize;
        Array.Copy(bytes, c1, swapped, c0, VaultContent.ChunkSize); // chunk1 -> slot0
        Array.Copy(bytes, c0, swapped, c1, VaultContent.ChunkSize); // chunk0 -> slot1

        using var swappedStream = new MemoryStream(swapped);
        using var target = new MemoryStream();
        Assert.Throws<VaultIntegrityException>(() => VaultContent.DecryptStream(keys, swappedStream, target));
    }

    [Fact]
    public void EncryptStream_UsesAUniqueNoncePerChunk()
    {
        // Finding 1.1 invariant: within one file every 12-byte GCM nonce (the
        // header nonce plus each chunk nonce) is distinct, so no (contentKey,
        // nonce) pair repeats. Nonces are random per the Cryptomator format;
        // this guards against an accidental future change to fixed nonces.
        byte[] plaintext = new byte[VaultContent.ChunkPayloadSize * 3 + 100]; // 4 chunks
        using var plainStream = new MemoryStream(plaintext);
        using var cipherStream = new MemoryStream();
        VaultContent.EncryptStream(keys, plainStream, cipherStream);

        byte[] bytes = cipherStream.ToArray();
        var nonces = new HashSet<string>
        {
            Convert.ToHexString(bytes.AsSpan(0, VaultContent.HeaderNonceSize)), // header nonce
        };

        int offset = VaultContent.HeaderSize;
        int chunks = 0;
        while (offset < bytes.Length)
        {
            int payload = Math.Min(VaultContent.ChunkPayloadSize, bytes.Length - offset - VaultContent.HeaderNonceSize - 16);
            string nonce = Convert.ToHexString(bytes.AsSpan(offset, VaultContent.HeaderNonceSize));
            Assert.True(nonces.Add(nonce), "duplicate GCM nonce within a single file");
            offset += VaultContent.HeaderNonceSize + payload + 16;
            chunks++;
        }

        Assert.Equal(4, chunks);
    }

    [Fact]
    public void DecryptStream_WithWrongKey_Throws()
    {
        using var plainStream = new MemoryStream(new byte[2048]);
        using var cipherStream = new MemoryStream();
        VaultContent.EncryptStream(keys, plainStream, cipherStream);

        using var otherKeys = new VaultKeys([0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60], [0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x10]);
        using var corruptedStream = new MemoryStream(cipherStream.ToArray());
        using var target = new MemoryStream();

        Assert.Throws<VaultIntegrityException>(() => VaultContent.DecryptStream(otherKeys, corruptedStream, target));
    }
}
