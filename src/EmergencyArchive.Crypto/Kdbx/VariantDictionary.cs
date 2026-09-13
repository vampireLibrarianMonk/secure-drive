using System.Text;

namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// The KDBX 4 "VariantDictionary": a small typed key/value map (used for the KDF
/// parameters). Serialised form: a uint16 version, then a sequence of entries
/// [type:1][keyLen:int32][key:UTF8][valueLen:int32][value], terminated by a
/// single 0x00 type byte.
/// </summary>
internal sealed class VariantDictionary
{
    private const ushort Version = 0x0100;
    private const ushort VersionMajorMask = 0xFF00;

    private enum EntryType : byte
    {
        End = 0x00,
        UInt32 = 0x04,
        UInt64 = 0x05,
        Bool = 0x08,
        Int32 = 0x0C,
        Int64 = 0x0D,
        String = 0x18,
        ByteArray = 0x42,
    }

    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    public bool TryGetUInt32(string key, out uint value)
    {
        if (values.TryGetValue(key, out object? o) && o is uint u)
        {
            value = u;
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryGetUInt64(string key, out ulong value)
    {
        if (values.TryGetValue(key, out object? o))
        {
            switch (o)
            {
                case ulong ul: value = ul; return true;
                case uint u: value = u; return true;
            }
        }

        value = 0;
        return false;
    }

    public bool TryGetByteArray(string key, out byte[] value)
    {
        if (values.TryGetValue(key, out object? o) && o is byte[] b)
        {
            value = b;
            return true;
        }

        value = [];
        return false;
    }

    public void SetUInt32(string key, uint value) => values[key] = value;

    public void SetUInt64(string key, ulong value) => values[key] = value;

    public void SetByteArray(string key, byte[] value) => values[key] = value;

    public static VariantDictionary Parse(byte[] data)
    {
        var reader = new KdbxReader(data);
        ushort version = reader.ReadUInt16();
        if ((version & VersionMajorMask) > (Version & VersionMajorMask))
        {
            throw new KdbxFormatException("Unsupported VariantDictionary version.");
        }

        var dict = new VariantDictionary();
        while (true)
        {
            byte typeByte = reader.ReadByte();
            if (typeByte == (byte)EntryType.End)
            {
                break;
            }

            int keyLen = reader.ReadInt32();
            string key = Encoding.UTF8.GetString(reader.ReadBytes(keyLen));
            int valueLen = reader.ReadInt32();
            byte[] valueBytes = reader.ReadBytes(valueLen);

            object value = (EntryType)typeByte switch
            {
                EntryType.UInt32 => BitConverter.ToUInt32(Require(valueBytes, 4), 0),
                EntryType.UInt64 => BitConverter.ToUInt64(Require(valueBytes, 8), 0),
                EntryType.Bool => valueBytes.Length == 1 && valueBytes[0] != 0,
                EntryType.Int32 => BitConverter.ToInt32(Require(valueBytes, 4), 0),
                EntryType.Int64 => BitConverter.ToInt64(Require(valueBytes, 8), 0),
                EntryType.String => Encoding.UTF8.GetString(valueBytes),
                EntryType.ByteArray => valueBytes,
                _ => throw new KdbxFormatException($"Unknown VariantDictionary entry type 0x{typeByte:X2}."),
            };

            dict.values[key] = value;
        }

        return dict;
    }

    public byte[] Serialize()
    {
        var writer = new KdbxWriter();
        writer.WriteUInt16(Version);
        foreach ((string key, object value) in values)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            switch (value)
            {
                case uint u:
                    WriteEntry(writer, EntryType.UInt32, keyBytes, BitConverter.GetBytes(u));
                    break;
                case ulong ul:
                    WriteEntry(writer, EntryType.UInt64, keyBytes, BitConverter.GetBytes(ul));
                    break;
                case int i:
                    WriteEntry(writer, EntryType.Int32, keyBytes, BitConverter.GetBytes(i));
                    break;
                case long l:
                    WriteEntry(writer, EntryType.Int64, keyBytes, BitConverter.GetBytes(l));
                    break;
                case bool bo:
                    WriteEntry(writer, EntryType.Bool, keyBytes, [(byte)(bo ? 1 : 0)]);
                    break;
                case string s:
                    WriteEntry(writer, EntryType.String, keyBytes, Encoding.UTF8.GetBytes(s));
                    break;
                case byte[] ba:
                    WriteEntry(writer, EntryType.ByteArray, keyBytes, ba);
                    break;
                default:
                    throw new KdbxFormatException($"Cannot serialise VariantDictionary value of type {value.GetType()}.");
            }
        }

        writer.WriteByte((byte)EntryType.End);
        return writer.ToArray();
    }

    private static void WriteEntry(KdbxWriter writer, EntryType type, byte[] key, byte[] value)
    {
        writer.WriteByte((byte)type);
        writer.WriteInt32(key.Length);
        writer.WriteBytes(key);
        writer.WriteInt32(value.Length);
        writer.WriteBytes(value);
    }

    private static byte[] Require(byte[] value, int length)
    {
        if (value.Length != length)
        {
            throw new KdbxFormatException($"VariantDictionary numeric value has wrong length {value.Length} (expected {length}).");
        }

        return value;
    }
}
