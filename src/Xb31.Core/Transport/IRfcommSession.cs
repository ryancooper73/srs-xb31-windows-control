namespace Xb31.Core.Transport;

internal interface IRfcommSession : IAsyncDisposable
{
    Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(int maximumBytes, CancellationToken cancellationToken);
}
