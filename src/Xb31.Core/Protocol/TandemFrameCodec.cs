using System.Buffers.Binary;

namespace Xb31.Core.Protocol;

public static class TandemFrameCodec
{
    public const int MaxUnescapedInnerLength = 65_536;

    private const byte StartDelimiter = 0x3E;
    private const byte EndDelimiter = 0x3C;
    private const byte Escape = 0x3D;
    private const int InnerOverhead = 7;
    private const int MaximumPayloadLength = MaxUnescapedInnerLength - InnerOverhead;

    public static byte[] EncodeData(ReadOnlySpan<byte> payload, byte sequence = 0) =>
        Encode(TandemFrameType.Data, sequence, payload);

    public static byte[] EncodeAck(byte sequence) =>
        Encode(TandemFrameType.Acknowledgement, sequence, []);

    public static TandemFrame Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < InnerOverhead + 2 ||
            encoded[0] != StartDelimiter ||
            encoded[^1] != EndDelimiter)
        {
            throw new FormatException("Tandem frame delimiters are invalid.");
        }

        var inner = new List<byte>(Math.Min(encoded.Length - 2, MaxUnescapedInnerLength));
        for (int index = 1; index < encoded.Length - 1; index++)
        {
            byte value = encoded[index];
            if (value == Escape)
            {
                if (++index >= encoded.Length - 1)
                    throw new FormatException("Tandem frame ends with an incomplete escape sequence.");

                value = encoded[index] switch
                {
                    0x2C => EndDelimiter,
                    0x2D => Escape,
                    0x2E => StartDelimiter,
                    _ => throw new FormatException("Tandem frame contains an invalid escape sequence.")
                };
            }
            else if (value is EndDelimiter or StartDelimiter)
            {
                throw new FormatException("Tandem frame contains an unescaped delimiter.");
            }

            if (inner.Count == MaxUnescapedInnerLength)
                throw new FormatException("Tandem frame exceeds the maximum inner length.");

            inner.Add(value);
        }

        if (inner.Count < InnerOverhead)
            throw new FormatException("Tandem frame is shorter than its header.");

        TandemFrameType type = inner[0] switch
        {
            (byte)TandemFrameType.Data => TandemFrameType.Data,
            (byte)TandemFrameType.Acknowledgement => TandemFrameType.Acknowledgement,
            _ => throw new FormatException("Tandem frame type is invalid.")
        };

        byte sequence = inner[1];
        ValidateDecodedSequence(sequence);

        uint declaredLength = BinaryPrimitives.ReadUInt32BigEndian(
            new byte[] { inner[2], inner[3], inner[4], inner[5] });
        if ((ulong)inner.Count != (ulong)declaredLength + InnerOverhead)
            throw new FormatException("Tandem payload length does not match the frame header.");
        if (type == TandemFrameType.Acknowledgement && declaredLength != 0)
            throw new FormatException("Tandem acknowledgement frames cannot contain a payload.");

        byte checksum = 0;
        for (int index = 0; index < inner.Count - 1; index++)
            checksum = unchecked((byte)(checksum + inner[index]));
        if (checksum != inner[^1])
            throw new FormatException("Tandem frame checksum is invalid.");

        byte[] payload = inner.GetRange(6, checked((int)declaredLength)).ToArray();
        return new TandemFrame(type, sequence, payload);
    }

    private static byte[] Encode(
        TandemFrameType type,
        byte sequence,
        ReadOnlySpan<byte> payload)
    {
        ValidateEncodingSequence(sequence);
        if (payload.Length > MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"Payload cannot exceed {MaximumPayloadLength} bytes.");
        }

        byte[] inner = new byte[payload.Length + InnerOverhead];
        inner[0] = (byte)type;
        inner[1] = sequence;
        BinaryPrimitives.WriteUInt32BigEndian(inner.AsSpan(2, 4), (uint)payload.Length);
        payload.CopyTo(inner.AsSpan(6));

        byte checksum = 0;
        for (int index = 0; index < inner.Length - 1; index++)
            checksum = unchecked((byte)(checksum + inner[index]));
        inner[^1] = checksum;

        var encoded = new List<byte>(inner.Length + 2) { StartDelimiter };
        foreach (byte value in inner)
        {
            switch (value)
            {
                case EndDelimiter:
                    encoded.Add(Escape);
                    encoded.Add(0x2C);
                    break;
                case Escape:
                    encoded.Add(Escape);
                    encoded.Add(0x2D);
                    break;
                case StartDelimiter:
                    encoded.Add(Escape);
                    encoded.Add(0x2E);
                    break;
                default:
                    encoded.Add(value);
                    break;
            }
        }

        encoded.Add(EndDelimiter);
        return encoded.ToArray();
    }

    private static void ValidateEncodingSequence(byte sequence)
    {
        if (sequence > 1)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be zero or one.");
    }

    private static void ValidateDecodedSequence(byte sequence)
    {
        if (sequence > 1)
            throw new FormatException("Tandem frame sequence must be zero or one.");
    }
}
