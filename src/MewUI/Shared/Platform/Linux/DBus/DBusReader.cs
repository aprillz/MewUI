using System.Buffers.Binary;
using System.Text;

namespace Aprillz.MewUI.Platform.Linux.DBus;

/// <summary>
/// Reads D-Bus wire format from a byte buffer (little-endian only).
/// </summary>
internal ref struct DBusReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public DBusReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public DBusReader(byte[] data, int offset)
    {
        _data = data.AsSpan();
        _pos = offset;
    }

    public int Position => _pos;

    public int Remaining => _data.Length - _pos;

    public byte ReadByte()
    {
        return _data[_pos++];
    }

    public bool ReadBool()
    {
        Align(4);
        uint val = BinaryPrimitives.ReadUInt32LittleEndian(_data[_pos..]);
        _pos += 4;
        return val != 0;
    }

    public int ReadInt32()
    {
        Align(4);
        int val = BinaryPrimitives.ReadInt32LittleEndian(_data[_pos..]);
        _pos += 4;
        return val;
    }

    public uint ReadUInt32()
    {
        Align(4);
        uint val = BinaryPrimitives.ReadUInt32LittleEndian(_data[_pos..]);
        _pos += 4;
        return val;
    }

    public string ReadString()
    {
        Align(4);
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(_data[_pos..]);
        _pos += 4;
        string val = Encoding.UTF8.GetString(_data.Slice(_pos, (int)len));
        _pos += (int)len + 1; // skip NUL
        return val;
    }

    public string ReadObjectPath() => ReadString();

    public string ReadSignature()
    {
        byte len = _data[_pos++];
        string val = Encoding.UTF8.GetString(_data.Slice(_pos, len));
        _pos += len + 1; // skip NUL
        return val;
    }

    public void Align(int alignment)
    {
        int pad = (alignment - (_pos % alignment)) % alignment;
        _pos += pad;
    }

    public void Skip(int bytes)
    {
        _pos += bytes;
    }

    /// <summary>
    /// Skips a D-Bus value based on its type signature character.
    /// </summary>
    public void SkipValue(char sig)
    {
        switch (sig)
        {
            case 'y': _pos++; break;
            case 'b': Align(4); _pos += 4; break;
            case 'n': case 'q': Align(2); _pos += 2; break;
            case 'i': case 'u': Align(4); _pos += 4; break;
            case 'x': case 't': case 'd': Align(8); _pos += 8; break;
            case 's': case 'o': ReadString(); break;
            case 'g': ReadSignature(); break;
            case 'v': SkipVariant(); break;
            case 'a': SkipArray(); break;
            case '(': SkipStruct(); break;
            default: break; // unknown, can't skip safely
        }
    }

    private void SkipVariant()
    {
        string sig = ReadSignature();
        foreach (char c in sig)
            SkipValue(c);
    }

    private void SkipArray()
    {
        Align(4);
        uint arrayLen = ReadUInt32();
        // We don't know element alignment without the signature context,
        // but for simple skip purposes, just advance past the data.
        _pos += (int)arrayLen;
    }

    private void SkipStruct()
    {
        // Without knowing the full struct signature, we can't reliably skip.
        // This is best-effort for the IBus use case.
        Align(8);
    }

    /// <summary>
    /// Reads an IBus serialized object variant and extracts the text string.
    /// IBus serialization format: VARIANT containing STRUCT (sa{sv}sv)
    ///   s: type name ("IBusText")
    ///   a{sv}: serializable properties dict (usually empty)
    ///   s: the actual text
    ///   v: attributes (IBusAttrList)
    /// </summary>
    public string? ReadIBusText()
    {
        try
        {
            // Read the variant's inner type signature
            string sig = ReadSignature();

            // Align for struct
            Align(8);

            // Read type name
            string typeName = ReadString();
            if (typeName != "IBusText") return null;

            // Read a{sv} properties dict
            Align(4);
            uint dictLen = ReadUInt32();
            // Dict entries have alignment 8 - padding is always present, even for empty arrays
            Align(8);
            _pos += (int)dictLen;

            // Read the text string
            string text = ReadString();
            return text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a complete D-Bus message from a byte buffer.
    /// Returns null if not enough data. Sets <paramref name="consumed"/> to bytes used.
    /// </summary>
    public static DBusMessage? TryParse(byte[] buffer, int offset, int length, out int consumed)
    {
        consumed = 0;
        if (length < 16) return null; // minimum header size

        var r = new DBusReader(buffer, offset);

        byte endian = r.ReadByte();
        if (endian != DBusConstants.LittleEndian)
            return null; // we only handle little-endian

        byte msgType = r.ReadByte();
        byte flags = r.ReadByte();
        byte version = r.ReadByte();
        uint bodyLength = r.ReadUInt32();
        uint serial = r.ReadUInt32();

        // Header fields array length
        uint headerFieldsLength = r.ReadUInt32();

        // Total message size: 12 (fixed header) + 4 (array length) + headerFieldsLength + padding + bodyLength
        int headerEnd = 12 + 4 + (int)headerFieldsLength;
        int paddedHeaderEnd = (headerEnd + 7) & ~7; // align to 8
        int totalSize = paddedHeaderEnd + (int)bodyLength;

        if (length < totalSize) return null; // incomplete message

        // Parse header fields
        string? path = null, iface = null, member = null, errorName = null;
        string? dest = null, sender = null, signature = null;
        uint replySerial = 0;

        int fieldsEnd = r.Position + (int)headerFieldsLength;
        while (r.Position < fieldsEnd)
        {
            r.Align(8);
            if (r.Position >= fieldsEnd) break;

            byte code = r.ReadByte();
            string varSig = r.ReadSignature();

            switch (code)
            {
                case DBusConstants.FieldPath:
                    path = r.ReadObjectPath();
                    break;
                case DBusConstants.FieldInterface:
                    iface = r.ReadString();
                    break;
                case DBusConstants.FieldMember:
                    member = r.ReadString();
                    break;
                case DBusConstants.FieldErrorName:
                    errorName = r.ReadString();
                    break;
                case DBusConstants.FieldReplySerial:
                    replySerial = r.ReadUInt32();
                    break;
                case DBusConstants.FieldDestination:
                    dest = r.ReadString();
                    break;
                case DBusConstants.FieldSender:
                    sender = r.ReadString();
                    break;
                case DBusConstants.FieldSignature:
                    signature = r.ReadSignature();
                    break;
                default:
                    // Skip unknown field value
                    foreach (char c in varSig)
                        r.SkipValue(c);
                    break;
            }
        }

        // Extract body
        byte[] body = new byte[bodyLength];
        Buffer.BlockCopy(buffer, offset + paddedHeaderEnd, body, 0, (int)bodyLength);

        consumed = totalSize;
        return new DBusMessage
        {
            Type = msgType,
            Flags = flags,
            Serial = serial,
            ReplySerial = replySerial,
            Path = path,
            Interface = iface,
            Member = member,
            ErrorName = errorName,
            Destination = dest,
            Sender = sender,
            Signature = signature,
            Body = body,
        };
    }
}
