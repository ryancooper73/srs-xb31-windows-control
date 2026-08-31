using Xb31.Core.Protocol;

namespace Xb31.Core.Transport;

internal sealed class Xb31Transport : IXb31Transport
{
    private readonly IRfcommPlatform _platform;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _responseTimeout;

    internal Xb31Transport(IRfcommPlatform platform)
        : this(
            platform,
            Task.Delay,
            Xb31Timeouts.Response,
            () => DateTimeOffset.Now)
    {
    }

    internal Xb31Transport(
        IRfcommPlatform platform,
        Func<TimeSpan, CancellationToken, Task> delay)
        : this(
            platform,
            delay,
            Xb31Timeouts.Response,
            () => DateTimeOffset.Now)
    {
    }

    internal Xb31Transport(
        IRfcommPlatform platform,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan responseTimeout)
        : this(
            platform,
            delay,
            responseTimeout,
            () => DateTimeOffset.Now)
    {
    }

    internal Xb31Transport(
        IRfcommPlatform platform,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan responseTimeout,
        Func<DateTimeOffset> now) =>
        (_platform, _delay, _responseTimeout, _now) = (
            platform ?? throw new ArgumentNullException(nameof(platform)),
            delay ?? throw new ArgumentNullException(nameof(delay)),
            responseTimeout,
            now ?? throw new ArgumentNullException(nameof(now)));

    public Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(null, cancellationToken);

    public Task<Xb31Result> SendAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken) =>
        ExecuteAsync(frame, cancellationToken);

    public async Task<Xb31TransportResponse> RequestAsync(
        ReadOnlyMemory<byte> requestFrame,
        byte expectedResponseCommand,
        CancellationToken cancellationToken)
    {
        IRfcommSession? session = null;
        Xb31TransportResponse response;

        try
        {
            session = await _platform
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            await session.WriteAsync(requestFrame, cancellationToken).ConfigureAwait(false);
            byte[] payload = await ReceiveResponseAsync(
                    session,
                    expectedResponseCommand,
                    cancellationToken)
                .ConfigureAwait(false);
            response = new Xb31TransportResponse(Xb31Result.Success, payload);
        }
        catch (Exception exception)
        {
            response = new Xb31TransportResponse(Failure(exception), null);
        }

        if (session is null)
            return response;

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (response.Result.IsSuccess)
        {
            return new Xb31TransportResponse(
                new Xb31Result(Xb31Status.CleanupFailed, exception),
                response.Payload);
        }
        catch
        {
            // Preserve the operation failure, as the send path does.
        }

        return response;
    }

    public async Task<Xb31TransportBatchResponse> RequestInitializedAsync(
        IReadOnlyList<Xb31TransportRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);

        IRfcommSession? session = null;
        var payloads = Enumerable.Repeat<byte[]?>(null, requests.Count).ToArray();
        Xb31Result result;

        try
        {
            session = await _platform.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var conversation = new TandemConversation(session);
            byte sequence = await InitializeAsync(conversation, cancellationToken).ConfigureAwait(false);

            for (int index = 0; index < requests.Count; index++)
            {
                Xb31TransportRequest request = requests[index];
                if (request.Payload is null || request.ExpectedResponsePrefix is null ||
                    request.ExpectedResponsePrefix.Length == 0)
                {
                    throw new ArgumentException("Status requests require a payload and response prefix.", nameof(requests));
                }

                await session.WriteAsync(
                    TandemFrameCodec.EncodeData(request.Payload, sequence),
                    cancellationToken).ConfigureAwait(false);
                sequence ^= 0x01;
                payloads[index] = await ReadMatchingDataAsync(
                    conversation,
                    request.ExpectedResponsePrefix,
                    cancellationToken).ConfigureAwait(false);
            }

            result = Xb31Result.Success;
        }
        catch (Exception exception)
        {
            result = Failure(exception);
        }

        if (session is null)
            return new Xb31TransportBatchResponse(result, payloads);

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (result.IsSuccess)
        {
            result = new Xb31Result(Xb31Status.CleanupFailed, exception);
        }
        catch
        {
            // Preserve the operation failure.
        }

        return new Xb31TransportBatchResponse(result, payloads);
    }

    private async Task<byte> InitializeAsync(
        TandemConversation conversation,
        CancellationToken cancellationToken)
    {
        await ReadMatchingDataAsync(conversation, new byte[] { 0x00 }, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _now();
        byte[][] initializationPayloads =
        [
            [0x0F, 0x01],
            [0x01, ToBcd(now.Hour), ToBcd(now.Minute), ToBcd(now.Second)],
            [0x05, 0x00, 0x00, 0x01],
            [0x0F, 0x02]
        ];

        byte sequence = 0;
        foreach (byte[] payload in initializationPayloads)
        {
            await conversation.Session.WriteAsync(
                TandemFrameCodec.EncodeData(payload, sequence),
                cancellationToken).ConfigureAwait(false);
            await ReadAcknowledgementAsync(
                conversation,
                (byte)(sequence ^ 0x01),
                cancellationToken).ConfigureAwait(false);
            sequence ^= 0x01;
        }

        await ReadMatchingDataAsync(conversation, new byte[] { 0x02, 0x01 }, cancellationToken)
            .ConfigureAwait(false);
        await ReadMatchingDataAsync(
            conversation,
            new byte[] { 0x0C, 0x31, 0x02 },
            cancellationToken).ConfigureAwait(false);
        await ReadMatchingDataAsync(conversation, new byte[] { 0x03 }, cancellationToken)
            .ConfigureAwait(false);

        await conversation.Session.WriteAsync(
            TandemFrameCodec.EncodeData([0x0F, 0x00], sequence),
            cancellationToken).ConfigureAwait(false);
        await ReadAcknowledgementAsync(
            conversation,
            (byte)(sequence ^ 0x01),
            cancellationToken).ConfigureAwait(false);

        return (byte)(sequence ^ 0x01);
    }

    private async Task ReadAcknowledgementAsync(
        TandemConversation conversation,
        byte expectedSequence,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_responseTimeout);
        while (true)
        {
            TandemFrame frame = await conversation.ReadNextAsync(timeout.Token).ConfigureAwait(false);
            if (frame.Type == TandemFrameType.Acknowledgement)
            {
                if (frame.Sequence == expectedSequence)
                    return;
                continue;
            }

            await AcknowledgeAsync(conversation.Session, frame, timeout.Token).ConfigureAwait(false);
        }
    }

    private async Task<byte[]> ReadMatchingDataAsync(
        TandemConversation conversation,
        ReadOnlyMemory<byte> expectedPrefix,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_responseTimeout);
        while (true)
        {
            TandemFrame frame = await conversation.ReadNextAsync(timeout.Token).ConfigureAwait(false);
            if (frame.Type == TandemFrameType.Acknowledgement)
                continue;

            await AcknowledgeAsync(conversation.Session, frame, timeout.Token).ConfigureAwait(false);
            if (frame.Payload.AsSpan().StartsWith(expectedPrefix.Span))
                return frame.Payload;
        }
    }

    private static Task AcknowledgeAsync(
        IRfcommSession session,
        TandemFrame frame,
        CancellationToken cancellationToken) =>
        session.WriteAsync(
            TandemFrameCodec.EncodeAck((byte)(frame.Sequence ^ 0x01)),
            cancellationToken);

    private static byte ToBcd(int value) =>
        checked((byte)(((value / 10) << 4) | (value % 10)));

    private async Task<byte[]> ReceiveResponseAsync(
        IRfcommSession session,
        byte expectedResponseCommand,
        CancellationToken cancellationToken)
    {
        using var responseTimeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        responseTimeout.CancelAfter(_responseTimeout);
        var parser = new TandemFrameParser();

        while (true)
        {
            byte[] chunk = await session
                .ReadAsync(Xb31Timeouts.ReadChunkSize, responseTimeout.Token)
                .ConfigureAwait(false);
            if (chunk.Length == 0)
                throw new Xb31TransportException(Xb31Status.ReadFailed);

            for (int index = 0; index < chunk.Length; index++)
            {
                try
                {
                    parser.Append(chunk.AsSpan(index, 1));
                }
                catch (FormatException exception)
                {
                    throw new Xb31TransportException(
                        Xb31Status.MalformedResponse,
                        exception);
                }

                while (parser.TryRead(out TandemFrame? frame))
                {
                    if (frame!.Type == TandemFrameType.Acknowledgement)
                        continue;

                    await session
                        .WriteAsync(
                            TandemFrameCodec.EncodeAck((byte)(frame.Sequence ^ 0x01)),
                            responseTimeout.Token)
                        .ConfigureAwait(false);

                    if (frame.Payload.Length > 0 &&
                        frame.Payload[0] == expectedResponseCommand)
                    {
                        return frame.Payload;
                    }
                }
            }
        }
    }

    private async Task<Xb31Result> ExecuteAsync(
        ReadOnlyMemory<byte>? frame,
        CancellationToken cancellationToken)
    {
        IRfcommSession? session = null;
        Xb31Result result;

        try
        {
            session = await _platform
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            if (frame is { } command)
            {
                await session.WriteAsync(command, cancellationToken).ConfigureAwait(false);
                await _delay(Xb31Timeouts.CommandSettle, cancellationToken)
                    .ConfigureAwait(false);
            }

            result = Xb31Result.Success;
        }
        catch (Exception exception)
        {
            result = Failure(exception);
        }

        if (session is null)
            return result;

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (result.IsSuccess)
        {
            return new Xb31Result(Xb31Status.CleanupFailed, exception);
        }
        catch
        {
            // Preserve the operation failure, as the baseline helper did.
        }

        return result;
    }

    private static Xb31Result Failure(Exception exception) => exception switch
    {
        Xb31TransportException
        {
            Status: Xb31Status.MalformedResponse,
            InnerException: FormatException formatException
        } => new Xb31Result(Xb31Status.MalformedResponse, formatException),
        Xb31TransportException transportException =>
            new Xb31Result(transportException.Status, transportException),
        OperationCanceledException => new Xb31Result(Xb31Status.Timeout, exception),
        _ => new Xb31Result(Xb31Status.UnexpectedFailure, exception)
    };

    private sealed class TandemConversation(IRfcommSession session)
    {
        private readonly TandemFrameParser _parser = new();
        private readonly Queue<TandemFrame> _frames = [];
        private readonly Queue<byte> _pendingBytes = [];
        private readonly byte[] _singleByte = new byte[1];

        internal IRfcommSession Session { get; } = session;

        internal async Task<TandemFrame> ReadNextAsync(CancellationToken cancellationToken)
        {
            while (_frames.Count == 0)
            {
                if (_pendingBytes.Count == 0)
                {
                    byte[] chunk = await Session
                        .ReadAsync(Xb31Timeouts.ReadChunkSize, cancellationToken)
                        .ConfigureAwait(false);
                    if (chunk.Length == 0)
                        throw new Xb31TransportException(Xb31Status.ReadFailed);
                    foreach (byte value in chunk)
                        _pendingBytes.Enqueue(value);
                }

                try
                {
                    _singleByte[0] = _pendingBytes.Dequeue();
                    _parser.Append(_singleByte);
                }
                catch (FormatException exception)
                {
                    throw new Xb31TransportException(Xb31Status.MalformedResponse, exception);
                }

                while (_parser.TryRead(out TandemFrame? frame))
                    _frames.Enqueue(frame!);
            }

            return _frames.Dequeue();
        }
    }
}
