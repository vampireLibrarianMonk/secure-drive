using EmergencyArchive.Crypto.Kdbx;
using Xunit;

namespace EmergencyArchive.Crypto.Tests;

/// <summary>
/// Tests for the KDBX low-level binary layer: the little-endian reader/writer
/// and the KDBX4 VariantDictionary (KDF parameter container). These pin the
/// framing before the format readers depend on it.
/// </summary>
public class KdbxBinaryTests
{
    [Fact]
    public void Reader_ReadsLittleEndianIntegers()
    {
        var writer = new KdbxWriter();
        writer.WriteUInt16(0x0201);
        writer.WriteUInt32(0x04030201);
        writer.WriteUInt64(0x0807060504030201);
        writer.WriteBytes([0xAA, 0xBB]);

        var reader = new KdbxReader(writer.ToArray());
        Assert.Equal(0x0201, reader.ReadUInt16());
        Assert.Equal(0x04030201u, reader.ReadUInt32());
        Assert.Equal(0x0807060504030201ul, reader.ReadUInt64());
        Assert.Equal([0xAA, 0xBB], reader.ReadBytes(2));
        Assert.True(reader.AtEnd);
    }

    [Fact]
    public void Reader_OverReadThrowsFormatException()
    {
        var reader = new KdbxReader([0x01, 0x02]);
        Assert.Throws<KdbxFormatException>(() => reader.ReadUInt32());
    }

    [Fact]
    public void VariantDictionary_RoundTrips()
    {
        var dict = new VariantDictionary();
        dict.SetUInt32("p", 4);
        dict.SetUInt64("m", 67108864);
        dict.SetByteArray("S", [1, 2, 3, 4, 5]);

        byte[] serialized = dict.Serialize();
        VariantDictionary parsed = VariantDictionary.Parse(serialized);

        Assert.True(parsed.TryGetUInt32("p", out uint p));
        Assert.Equal(4u, p);
        Assert.True(parsed.TryGetUInt64("m", out ulong m));
        Assert.Equal(67108864ul, m);
        Assert.True(parsed.TryGetByteArray("S", out byte[] s));
        Assert.Equal([1, 2, 3, 4, 5], s);
    }

    [Fact]
    public void VariantDictionary_UInt32ReadableAsUInt64()
    {
        // KDBX iteration counts may be stored as UInt32 or UInt64; the reader
        // should accept either when asked for a 64-bit value.
        var dict = new VariantDictionary();
        dict.SetUInt32("I", 10);

        VariantDictionary parsed = VariantDictionary.Parse(dict.Serialize());
        Assert.True(parsed.TryGetUInt64("I", out ulong i));
        Assert.Equal(10ul, i);
    }

    [Fact]
    public void VariantDictionary_MissingKeyReturnsFalse()
    {
        VariantDictionary parsed = VariantDictionary.Parse(new VariantDictionary().Serialize());
        Assert.False(parsed.TryGetUInt32("nope", out _));
        Assert.False(parsed.TryGetByteArray("nope", out _));
    }
}
