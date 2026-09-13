namespace EmergencyArchive.Crypto.Kdbx;

/// <summary>
/// The parsed, unencrypted KDBX outer header. Holds the fields common to both
/// KDBX 3.1 and 4.x plus the raw header bytes (needed for the KDBX4 header
/// SHA-256 and HMAC-SHA256, and the KDBX3 payload framing). Only the fields the
/// importer/exporter needs are surfaced.
/// </summary>
internal sealed class KdbxHeader
{
    public uint MajorVersion { get; init; }

    public uint MinorVersion { get; init; }

    public byte[] CipherId { get; set; } = [];

    public KdbxFormat.CompressionAlgorithm Compression { get; set; } = KdbxFormat.CompressionAlgorithm.GZip;

    public byte[] MasterSeed { get; set; } = [];

    public byte[] EncryptionIv { get; set; } = [];

    // KDBX 3.1 only.
    public byte[] TransformSeed { get; set; } = [];
    public ulong TransformRounds { get; set; }
    public byte[] ProtectedStreamKey { get; set; } = [];
    public byte[] StreamStartBytes { get; set; } = [];
    public KdbxFormat.InnerStreamAlgorithm InnerStreamId { get; set; } = KdbxFormat.InnerStreamAlgorithm.Salsa20;

    // KDBX 4 only.
    public VariantDictionary? KdfParameters { get; set; }

    /// <summary>The exact header bytes from the file start through the end-of-header marker.</summary>
    public byte[] RawHeaderBytes { get; set; } = [];

    public bool IsVersion4 => MajorVersion == KdbxFormat.FileVersion4Major;

    public bool IsVersion3 => MajorVersion == KdbxFormat.FileVersion3Major;

    /// <summary>
    /// Reads the signatures, version, and outer-header TLV fields. Leaves the
    /// reader positioned immediately after the end-of-header marker (the start
    /// of the encrypted payload for KDBX4, or the payload for KDBX3). Field
    /// length is a uint16 in KDBX3 and a uint32 in KDBX4.
    /// </summary>
    public static KdbxHeader Parse(KdbxReader reader)
    {
        uint sig1 = reader.ReadUInt32();
        uint sig2 = reader.ReadUInt32();
        if (sig1 != KdbxFormat.Signature1 || sig2 != KdbxFormat.Signature2)
        {
            throw new KdbxFormatException("Not a KeePass KDBX file (bad signature).");
        }

        ushort minor = reader.ReadUInt16();
        uint major = reader.ReadUInt16();
        if (major is not (KdbxFormat.FileVersion3Major or KdbxFormat.FileVersion4Major))
        {
            throw new KdbxFormatException($"Unsupported KDBX version {major}.{minor} (only 3.x and 4.x are supported).");
        }

        var header = new KdbxHeader { MajorVersion = major, MinorVersion = minor };
        bool v4 = header.IsVersion4;

        while (true)
        {
            byte fieldId = reader.ReadByte();
            int length = v4 ? reader.ReadInt32() : reader.ReadUInt16();
            byte[] fieldData = reader.ReadBytes(length);

            var id = (KdbxFormat.HeaderFieldId)fieldId;
            if (id == KdbxFormat.HeaderFieldId.EndOfHeader)
            {
                break;
            }

            header.ApplyField(id, fieldData);
        }

        header.RawHeaderBytes = reader.Slice(0, reader.Position);
        return header;
    }

    private void ApplyField(KdbxFormat.HeaderFieldId id, byte[] data)
    {
        switch (id)
        {
            case KdbxFormat.HeaderFieldId.Comment:
                break; // ignored
            case KdbxFormat.HeaderFieldId.CipherId:
                if (data.Length != 16)
                {
                    throw new KdbxFormatException("KDBX CipherID is not a 16-byte UUID.");
                }

                CipherId = data;
                break;
            case KdbxFormat.HeaderFieldId.CompressionFlags:
                Compression = (KdbxFormat.CompressionAlgorithm)BitConverter.ToUInt32(Require(data, 4), 0);
                break;
            case KdbxFormat.HeaderFieldId.MasterSeed:
                MasterSeed = data;
                break;
            case KdbxFormat.HeaderFieldId.TransformSeed:
                TransformSeed = data;
                break;
            case KdbxFormat.HeaderFieldId.TransformRounds:
                TransformRounds = BitConverter.ToUInt64(Require(data, 8), 0);
                break;
            case KdbxFormat.HeaderFieldId.EncryptionIv:
                EncryptionIv = data;
                break;
            case KdbxFormat.HeaderFieldId.ProtectedStreamKey:
                ProtectedStreamKey = data;
                break;
            case KdbxFormat.HeaderFieldId.StreamStartBytes:
                StreamStartBytes = data;
                break;
            case KdbxFormat.HeaderFieldId.InnerRandomStreamId:
                InnerStreamId = (KdbxFormat.InnerStreamAlgorithm)BitConverter.ToUInt32(Require(data, 4), 0);
                break;
            case KdbxFormat.HeaderFieldId.KdfParameters:
                KdfParameters = VariantDictionary.Parse(data);
                break;
            case KdbxFormat.HeaderFieldId.PublicCustomData:
                break; // preserved elsewhere if needed; not used by the importer
            default:
                break; // unknown fields are ignored for forward compatibility
        }
    }

    private static byte[] Require(byte[] value, int length)
    {
        if (value.Length != length)
        {
            throw new KdbxFormatException($"KDBX header field has wrong length {value.Length} (expected {length}).");
        }

        return value;
    }
}
