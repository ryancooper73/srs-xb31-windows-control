namespace Xb31.Core;

public enum Xb31Status
{
    Success,
    Unavailable,
    ConnectionFailed,
    WriteFailed,
    Timeout,
    MalformedCommand,
    UnexpectedFailure,
    CleanupFailed,
    ReadFailed,
    MalformedResponse
}
