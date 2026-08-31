using Xb31.Core.Protocol;

namespace Xb31.Core;

public static class Xb31FrameBuilder
{
    public static byte[] Build(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            throw new ArgumentException("Payload cannot be empty.", nameof(payload));

        return TandemFrameCodec.EncodeData(payload, 0);
    }
}
