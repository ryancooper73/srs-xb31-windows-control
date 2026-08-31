using Windows.Storage.Streams;
using Xb31.Core.Transport;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class DataReaderChunkReaderTests
{
    [TestMethod]
    public async Task ReadAsync_ReturnsAtMostRequestedBytesAndPreservesRemainder()
    {
        byte[] payload = Enumerable.Range(0, 700).Select(value => (byte)value).ToArray();
        using var stream = await CreateStreamAsync(payload);
        using var reader = new DataReaderChunkReader(stream.GetInputStreamAt(0));

        byte[] first = await reader.ReadAsync(512, CancellationToken.None);
        byte[] second = await reader.ReadAsync(512, CancellationToken.None);

        CollectionAssert.AreEqual(payload[..512], first);
        CollectionAssert.AreEqual(payload[512..], second);
    }

    [TestMethod]
    public async Task ReadAsync_ReturnsEmptyArrayAtEndOfStream()
    {
        using var stream = new InMemoryRandomAccessStream();
        using var reader = new DataReaderChunkReader(stream.GetInputStreamAt(0));

        byte[] chunk = await reader.ReadAsync(512, CancellationToken.None);

        Assert.IsEmpty(chunk);
    }

    [TestMethod]
    public async Task ReadAsync_RejectsNonPositiveMaximum()
    {
        using var stream = new InMemoryRandomAccessStream();
        using var reader = new DataReaderChunkReader(stream.GetInputStreamAt(0));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => reader.ReadAsync(0, CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_ObservesCancellationBeforeReading()
    {
        using var stream = await CreateStreamAsync([0x3E, 0x3C]);
        using var reader = new DataReaderChunkReader(stream.GetInputStreamAt(0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.ReadAsync(512, cancellation.Token));
    }

    private static async Task<InMemoryRandomAccessStream> CreateStreamAsync(byte[] payload)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(payload);
        await writer.StoreAsync();
        writer.DetachStream();
        return stream;
    }
}
