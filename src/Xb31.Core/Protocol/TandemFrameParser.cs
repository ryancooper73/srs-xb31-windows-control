namespace Xb31.Core.Protocol;

public sealed class TandemFrameParser
{
    private const byte StartDelimiter = 0x3E;
    private const byte EndDelimiter = 0x3C;
    private const byte Escape = 0x3D;

    private readonly List<byte> _encodedFrame = [];
    private readonly Queue<TandemFrame> _completedFrames = [];
    private bool _inFrame;
    private bool _escapePending;
    private int _unescapedInnerLength;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (!_inFrame)
            {
                if (value == StartDelimiter)
                    StartFrame();
                continue;
            }

            if (_escapePending)
            {
                _encodedFrame.Add(value);
                if (value is not (0x2C or 0x2D or 0x2E))
                    Reject("Tandem frame contains an invalid escape sequence.");

                _escapePending = false;
                CountInnerByte();
                continue;
            }

            if (value == StartDelimiter)
                Reject("Tandem frame contains a nested start delimiter.");

            if (value == EndDelimiter)
            {
                _encodedFrame.Add(value);
                byte[] completed = _encodedFrame.ToArray();
                ResetCurrentFrame();
                _completedFrames.Enqueue(TandemFrameCodec.Decode(completed));
                continue;
            }

            _encodedFrame.Add(value);
            if (value == Escape)
            {
                _escapePending = true;
                continue;
            }

            CountInnerByte();
        }
    }

    public bool TryRead(out TandemFrame? frame)
    {
        if (_completedFrames.Count == 0)
        {
            frame = null;
            return false;
        }

        frame = _completedFrames.Dequeue();
        return true;
    }

    private void StartFrame()
    {
        _inFrame = true;
        _encodedFrame.Add(StartDelimiter);
    }

    private void CountInnerByte()
    {
        _unescapedInnerLength++;
        if (_unescapedInnerLength > TandemFrameCodec.MaxUnescapedInnerLength)
            Reject("Tandem frame exceeds the maximum inner length.");
    }

    private void Reject(string message)
    {
        ResetCurrentFrame();
        throw new FormatException(message);
    }

    private void ResetCurrentFrame()
    {
        _encodedFrame.Clear();
        _inFrame = false;
        _escapePending = false;
        _unescapedInnerLength = 0;
    }
}
