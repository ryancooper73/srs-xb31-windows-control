namespace Xb31.Core.Protocol;

public enum TandemFrameType : byte
{
    Data = 0x00,
    Acknowledgement = 0x01
}

public sealed record TandemFrame(TandemFrameType Type, byte Sequence, byte[] Payload);
