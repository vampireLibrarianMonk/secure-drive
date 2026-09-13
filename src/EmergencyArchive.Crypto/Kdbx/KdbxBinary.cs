using System.Buffers.Binary;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// A minimal little-endian reader over a byte buffer for KDBX parsing. KDBX
/// stores every multi-byte integer little-endian; this reader tracks a position
/// and throws <see cref="KdbxFormatException"/> on any over-read so a truncated
/// or malformed file fails cleanly rather than reading garbage.
/// </summary>
internal sealed class KdbxReader(byte[] data, int position = 0)
{
    private readonly byte[] data = data;

    public int Position { get; private set; } = position;

    public int Length => data.Length;

    public bool AtEnd => Position >= data.Length;

    public byte ReadByte()
    {
        Require(1);
        return data[Position++];
    }

    public ushort ReadUInt16()
    {
        Require(2);
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Position, 2));
        Position += 2;
        return v;
    }

    public uint ReadUInt32()
    {
        Require(4);
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Position, 4));
        Position += 4;
        return v;
    }

    public ulong ReadUInt64()
    {
        Require(8);
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(Position, 8));
        Position += 8;
        return v;
    }

    public long ReadInt64()
    {
        Require(8);
        long v = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(Position, 8));
        Position += 8;
        return v;
    }

    public int ReadInt32()
    {
        Require(4);
        int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Position, 4));
        Position += 4;
        return v;
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0)
        {
            throw new KdbxFormatException("KDBX field length is negative.");
        }

        Require(count);
        byte[] slice = data.AsSpan(Position, count).ToArray();
        Position += count;
        return slice;
    }

    /// <summary>Returns the slice from a start offset up to (but not including) the current position.</summary>
    public byte[] Slice(int start, int endExclusive) => data.AsSpan(start, endExclusive - start).ToArray();

    /// <summary>Everything from the current position to the end of the buffer.</summary>
    public byte[] ReadRemaining()
    {
        byte[] rest = data.AsSpan(Position).ToArray();
        Position = data.Length;
        return rest;
    }

    private void Require(int count)
    {
        if (Position + count > data.Length)
        {
            throw new KdbxFormatException("KDBX file is truncated or malformed.");
        }
    }
}

/// <summary>Little-endian writer for producing KDBX bytes.</summary>
internal sealed class KdbxWriter
{
    private readonly MemoryStream stream = new();

    public long Length => stream.Length;

    public void WriteByte(byte value) => stream.WriteByte(value);

    public void WriteUInt16(ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, value);
        stream.Write(b);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        stream.Write(b);
    }

    public void WriteInt32(int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        stream.Write(b);
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        stream.Write(b);
    }

    public void WriteInt64(long value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, value);
        stream.Write(b);
    }

    public void WriteBytes(ReadOnlySpan<byte> value) => stream.Write(value);

    public byte[] ToArray() => stream.ToArray();
}
