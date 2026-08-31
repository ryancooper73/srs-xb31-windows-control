namespace Xb31.Core;

public sealed record Xb31QueryResult<T>(
    Xb31Status Status,
    bool HasValue,
    T Value,
    Exception? Diagnostic = null)
{
    public bool IsSuccess => Status == Xb31Status.Success;

    public static Xb31QueryResult<T> Success(T value) =>
        new(Xb31Status.Success, true, value);
}
