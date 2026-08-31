namespace Xb31.Core;

public sealed record Xb31SetResult<T>(
    T RequestedValue,
    Xb31Result SendResult,
    Xb31QueryResult<T>? ReadBack)
{
    public bool WasSent =>
        SendResult.Status is Xb31Status.Success or Xb31Status.CleanupFailed;

    public bool IsConfirmed =>
        ReadBack is { HasValue: true } &&
        EqualityComparer<T>.Default.Equals(RequestedValue, ReadBack.Value);
}
