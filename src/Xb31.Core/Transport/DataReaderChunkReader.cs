using Windows.Storage.Streams;

namespace Xb31.Core.Transport;

internal sealed class DataReaderChunkReader : IDisposable
{
    private readonly DataReader _reader;

    internal DataReaderChunkReader(IInputStream inputStream)
    {
        _reader = new DataReader(inputStream)
        {
            InputStreamOptions = InputStreamOptions.Partial
        };
    }

    internal async Task<byte[]> ReadAsync(
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();

        uint loaded = await _reader.LoadAsync((uint)maximumBytes)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        var chunk = new byte[loaded];
        _reader.ReadBytes(chunk);
        return chunk;
    }

    public void Dispose() => _reader.Dispose();
}
