using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Xb31.Core.Transport;

internal sealed class WindowsRfcommSession : IRfcommSession
{
    private readonly BluetoothDevice _device;
    private readonly RfcommDeviceService _service;
    private readonly BluetoothDevice _serviceDevice;
    private readonly StreamSocket _socket;
    private readonly Action<string>? _report;
    private DataReaderChunkReader? _reader;
    private DataWriter? _writer;
    private int _disposed;

    internal WindowsRfcommSession(
        BluetoothDevice device,
        RfcommDeviceService service,
        BluetoothDevice serviceDevice,
        StreamSocket socket,
        Action<string>? report)
    {
        _device = device;
        _service = service;
        _serviceDevice = serviceDevice;
        _socket = socket;
        _report = report;
    }

    public async Task WriteAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        try
        {
            using var writeTimeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            writeTimeout.CancelAfter(Xb31Timeouts.Write);

            _writer ??= new DataWriter(_socket.OutputStream);
            _writer.WriteBytes(frame.ToArray());

            uint stored = await _writer.StoreAsync()
                .AsTask(writeTimeout.Token)
                .ConfigureAwait(false);
            if (stored != frame.Length)
            {
                throw new IOException(
                    $"RFCOMM write was incomplete: {stored}/{frame.Length} bytes.");
            }

            bool flushed = await _writer.FlushAsync()
                .AsTask(writeTimeout.Token)
                .ConfigureAwait(false);
            if (!flushed)
            {
                throw new IOException("RFCOMM flush failed.");
            }
        }
        catch (OperationCanceledException exception)
        {
            throw new Xb31TransportException(Xb31Status.Timeout, exception);
        }
        catch (Xb31TransportException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new Xb31TransportException(Xb31Status.WriteFailed, exception);
        }
    }

    public async Task<byte[]> ReadAsync(
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var responseTimeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            responseTimeout.CancelAfter(Xb31Timeouts.Response);

            _reader ??= new DataReaderChunkReader(_socket.InputStream);
            return await _reader.ReadAsync(maximumBytes, responseTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new Xb31TransportException(Xb31Status.Timeout, exception);
        }
        catch (Xb31TransportException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new Xb31TransportException(Xb31Status.ReadFailed, exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        List<Exception>? failures = null;
        Dispose(_writer, ref failures);
        Dispose(_reader, ref failures);
        Dispose(_socket, ref failures);
        Dispose(_serviceDevice, ref failures);
        Dispose(_service, ref failures);
        Dispose(_device, ref failures);
        _report?.Invoke("XB31: connection closed");

        return failures is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(new Xb31TransportException(
                Xb31Status.CleanupFailed,
                new AggregateException("XB31 cleanup failed.", failures)));
    }

    private static void Dispose(IDisposable? resource, ref List<Exception>? failures)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }
}
