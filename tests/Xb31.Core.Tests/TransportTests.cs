using Xb31.Core.Transport;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class TransportTests
{
    [TestMethod]
    public async Task SendAsync_ConnectsWritesOnceAndDisposes()
    {
        var session = new FakeSession();
        var platform = new FakePlatform(session);
        var transport = new Xb31Transport(platform);
        byte[] frame = [0x3E, 0x3C];

        Xb31Result result = await transport.SendAsync(frame, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, platform.ConnectCount);
        Assert.AreEqual(1, session.WriteCount);
        CollectionAssert.AreEqual(frame, session.LastFrame);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task SendAsync_KeepsSessionOpenDuringPostWriteSettleDelay()
    {
        var session = new FakeSession();
        var platform = new FakePlatform(session);
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan? requestedDelay = null;
        var transport = new Xb31Transport(
            platform,
            async (delay, cancellationToken) =>
            {
                requestedDelay = delay;
                delayStarted.SetResult();
                await releaseDelay.Task.WaitAsync(cancellationToken);
            });

        Task<Xb31Result> sendTask = transport.SendAsync(
            new byte[] { 0x3E, 0x3C },
            CancellationToken.None);
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(TimeSpan.FromSeconds(1), requestedDelay);
        Assert.AreEqual(1, session.WriteCount);
        Assert.AreEqual(0, session.DisposeCount);

        releaseDelay.SetResult();
        Xb31Result result = await sendTask;

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task ProbeAsync_ConnectsAndDisposesWithoutWriting()
    {
        var session = new FakeSession();
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31Result result = await transport.ProbeAsync(CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, session.WriteCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task SendAsync_DisposesAfterWriteFailure()
    {
        var session = new FakeSession
        {
            WriteException = new Xb31TransportException(Xb31Status.WriteFailed)
        };
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31Result result = await transport.SendAsync(new byte[] { 0x3E, 0x3C }, CancellationToken.None);

        Assert.AreEqual(Xb31Status.WriteFailed, result.Status);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task SendAsync_MapsCleanupFailureAfterCompletedWrite()
    {
        var session = new FakeSession { DisposeException = new IOException("cleanup failed") };
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31Result result = await transport.SendAsync(new byte[] { 0x3E, 0x3C }, CancellationToken.None);

        Assert.AreEqual(Xb31Status.CleanupFailed, result.Status);
        Assert.IsNotNull(result.Diagnostic);
        Assert.AreEqual(1, session.WriteCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task ProbeAsync_MapsCancellationToTimeout()
    {
        var platform = new FakePlatform(new OperationCanceledException());
        var transport = new Xb31Transport(platform);

        Xb31Result result = await transport.ProbeAsync(CancellationToken.None);

        Assert.AreEqual(Xb31Status.Timeout, result.Status);
    }

    [TestMethod]
    public async Task RequestAsync_MapsConnectFormatExceptionToUnexpectedFailure()
    {
        var exception = new FormatException("connect failed");
        var platform = new FakePlatform(exception);
        var transport = new Xb31Transport(platform);

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.UnexpectedFailure, response.Result.Status);
        Assert.AreSame(exception, response.Result.Diagnostic);
        Assert.IsNull(response.Payload);
        Assert.AreEqual(1, platform.ConnectCount);
    }

    [TestMethod]
    public async Task RequestInitializedAsync_ReplaysCapturedHandshakeAndReadsFourStatusesInOneSession()
    {
        var session = new FakeSession();
        session.QueueReadChunk(Convert.FromHexString(
            "3e00000000000c0050703001050100007a00007d3c" +
            "3e010100000000023c" +
            "3e010000000000013c" +
            "3e010100000000023c" +
            "3e010000000000013c" +
            "3e0001000000020201063c" +
            "3e00000000000406010203103c" +
            "3e0001000000080c31021fff210000873c" +
            "3e00000000000103043c" +
            "3e010100000000023c"));
        session.QueueReadChunk(Convert.FromHexString(
            "3e010000000000013c" +
            "3e000100000008f31112ff000000001e3c"));
        session.QueueReadChunk(Convert.FromHexString(
            "3e010100000000023c" +
            "3e000000000009921000ff0000000000aa3c" +
            "3e010000000000013c" +
            "3e000100000009f3121fff0000010101303c"));
        session.QueueReadChunk(Convert.FromHexString(
            "3e010100000000023c" +
            "3e000000000015f3123fff0001210d46756c6c792063686172676564813c"));
        var transport = new Xb31Transport(
            new FakePlatform(session),
            Task.Delay,
            TimeSpan.FromSeconds(1),
            () => new DateTimeOffset(2026, 8, 24, 20, 6, 55, TimeSpan.FromHours(2)));
        Xb31TransportRequest[] requests =
        [
            new(Convert.FromHexString("f2111fff"), Convert.FromHexString("f311")),
            new(Convert.FromHexString("91100fff00"), Convert.FromHexString("9210")),
            new(Convert.FromHexString("f2121fff"), Convert.FromHexString("f3121fff")),
            new(Convert.FromHexString("f2123fff"), Convert.FromHexString("f3123fff"))
        ];

        Xb31TransportBatchResponse response = await transport.RequestInitializedAsync(
            requests,
            CancellationToken.None);

        Assert.IsTrue(
            response.Result.IsSuccess,
            $"{response.Result.Status}: {response.Result.Diagnostic}");
        Assert.HasCount(4, response.Payloads);
        CollectionAssert.AreEqual(Convert.FromHexString("f31112ff00000000"), response.Payloads[0]);
        CollectionAssert.AreEqual(Convert.FromHexString("921000ff0000000000"), response.Payloads[1]);
        CollectionAssert.AreEqual(Convert.FromHexString("f3121fff0000010101"), response.Payloads[2]);
        CollectionAssert.AreEqual(
            Convert.FromHexString("f3123fff0001210d46756c6c792063686172676564"),
            response.Payloads[3]);
        Assert.AreEqual(1, session.DisposeCount);
        CollectionAssert.AreEqual(
            new[]
            {
                "3e010100000000023c",
                "3e0000000000020f01123c",
                "3e00010000000401200655813c",
                "3e000000000004050000010a3c",
                "3e0001000000020f02143c",
                "3e010000000000013c",
                "3e010100000000023c",
                "3e010000000000013c",
                "3e010100000000023c",
                "3e0000000000020f00113c",
                "3e000100000004f2111fff263c",
                "3e010000000000013c",
                "3e00000000000591100fff00b43c",
                "3e010100000000023c",
                "3e000100000004f2121fff273c",
                "3e010000000000013c",
                "3e000000000004f2123fff463c",
                "3e010100000000023c"
            },
            session.WrittenFrames.Select(Convert.ToHexString).Select(value => value.ToLowerInvariant()).ToArray());
    }

    [TestMethod]
    public async Task RequestInitializedAsync_ReturnsValidResponseBeforeLaterMalformedCoalescedFrame()
    {
        var session = new FakeSession();
        session.QueueReadChunk(Convert.FromHexString(
            "3e00000000000c0050703001050100007a00007d3c" +
            "3e010100000000023c" +
            "3e010000000000013c" +
            "3e010100000000023c" +
            "3e010000000000013c" +
            "3e0001000000020201063c" +
            "3e00000000000406010203103c" +
            "3e0001000000080c31021fff210000873c" +
            "3e00000000000103043c" +
            "3e010100000000023c"));
        session.QueueReadChunk(Convert.FromHexString(
            "3e010000000000013c" +
            "3e000100000008f31112ff000000001e3c" +
            "3e000000000009921000ff0000000000ab3c"));
        var transport = new Xb31Transport(
            new FakePlatform(session),
            Task.Delay,
            TimeSpan.FromSeconds(1),
            () => new DateTimeOffset(2026, 8, 24, 20, 6, 55, TimeSpan.FromHours(2)));

        Xb31TransportBatchResponse response = await transport.RequestInitializedAsync(
            [new Xb31TransportRequest(
                Convert.FromHexString("f2111fff"),
                Convert.FromHexString("f311"))],
            CancellationToken.None);

        Assert.IsTrue(response.Result.IsSuccess);
        CollectionAssert.AreEqual(
            Convert.FromHexString("f31112ff00000000"),
            response.Payloads.Single());
    }

    [TestMethod]
    public async Task RequestAsync_MapsRequestWriteFormatExceptionToUnexpectedFailure()
    {
        var exception = new FormatException("write failed");
        var session = new FakeSession { WriteException = exception };
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.UnexpectedFailure, response.Result.Status);
        Assert.AreSame(exception, response.Result.Diagnostic);
        Assert.IsNull(response.Payload);
        Assert.AreEqual(1, session.WriteCount);
        Assert.AreEqual(0, session.ReadCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_MapsReadFormatExceptionToUnexpectedFailure()
    {
        var exception = new FormatException("read failed");
        var session = new FakeSession();
        session.QueueReadException(exception);
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.UnexpectedFailure, response.Result.Status);
        Assert.AreSame(exception, response.Result.Diagnostic);
        Assert.IsNull(response.Payload);
        Assert.AreEqual(1, session.WriteCount);
        Assert.AreEqual(1, session.ReadCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_ReassemblesFragmentedResponseAndAcknowledgesNextSequence()
    {
        byte[] response = Convert.FromHexString(
            "3e000100000009921002ff0000000000ad3c");
        var session = new FakeSession();
        session.QueueReadChunk(response[..8]);
        session.QueueReadChunk(response[8..]);
        var platform = new FakePlatform(session);
        var transport = new Xb31Transport(platform);
        byte[] request = Convert.FromHexString("3e00000000000591100fff00b43c");

        Xb31TransportResponse responseResult = await transport.RequestAsync(
            request,
            0x92,
            CancellationToken.None);

        Assert.IsTrue(responseResult.Result.IsSuccess);
        CollectionAssert.AreEqual(
            Convert.FromHexString("921002ff0000000000"),
            responseResult.Payload);
        Assert.AreEqual(1, platform.ConnectCount);
        Assert.AreEqual(2, session.ReadCount);
        CollectionAssert.AreEqual(new[] { 512, 512 }, session.ReadMaximums);
        Assert.HasCount(2, session.WrittenFrames);
        CollectionAssert.AreEqual(request, session.WrittenFrames[0]);
        CollectionAssert.AreEqual(
            Convert.FromHexString("3e010000000000013c"),
            session.WrittenFrames[1]);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_ConsumesCoalescedAckAndAcknowledgesEachDataFrameBeforeMatch()
    {
        byte[] incoming =
        [
            .. Convert.FromHexString("3e010100000000023c"),
            .. Convert.FromHexString("3e000100000009f3121fff0000010101303c"),
            .. Convert.FromHexString("3e000000000009921002ff0000000000ac3c")
        ];
        var session = new FakeSession();
        session.QueueReadChunk(incoming);
        var transport = new Xb31Transport(new FakePlatform(session));
        byte[] request = Convert.FromHexString("3e00000000000591100fff00b43c");

        Xb31TransportResponse response = await transport.RequestAsync(
            request,
            0x92,
            CancellationToken.None);

        Assert.IsTrue(response.Result.IsSuccess);
        CollectionAssert.AreEqual(
            Convert.FromHexString("921002ff0000000000"),
            response.Payload);
        Assert.AreEqual(1, session.ReadCount);
        Assert.HasCount(3, session.WrittenFrames);
        CollectionAssert.AreEqual(request, session.WrittenFrames[0]);
        CollectionAssert.AreEqual(
            Convert.FromHexString("3e010000000000013c"),
            session.WrittenFrames[1]);
        CollectionAssert.AreEqual(
            Convert.FromHexString("3e010100000000023c"),
            session.WrittenFrames[2]);
    }

    [TestMethod]
    public async Task RequestAsync_ReturnsExpectedResponseBeforeLaterMalformedCoalescedFrame()
    {
        byte[] incoming = Convert.FromHexString(
            "3e000000000009921002ff0000000000ac3c" +
            "3e000100000009f3121fff0000010101313c");
        var session = new FakeSession();
        session.QueueReadChunk(incoming);
        var transport = new Xb31Transport(new FakePlatform(session));
        byte[] request = Convert.FromHexString("3e00000000000591100fff00b43c");

        Xb31TransportResponse response = await transport.RequestAsync(
            request,
            0x92,
            CancellationToken.None);

        Assert.IsTrue(response.Result.IsSuccess);
        CollectionAssert.AreEqual(
            Convert.FromHexString("921002ff0000000000"),
            response.Payload);
        Assert.AreEqual(1, session.ReadCount);
        Assert.HasCount(2, session.WrittenFrames);
        CollectionAssert.AreEqual(request, session.WrittenFrames[0]);
        CollectionAssert.AreEqual(
            Convert.FromHexString("3e010100000000023c"),
            session.WrittenFrames[1]);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_MapsMalformedCompletedFrameWithoutReturningPayload()
    {
        var session = new FakeSession();
        session.QueueReadChunk(Convert.FromHexString(
            "3e000000000009921002ff0000000000ad3c"));
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.MalformedResponse, response.Result.Status);
        Assert.IsInstanceOfType<FormatException>(response.Result.Diagnostic);
        Assert.IsNull(response.Payload);
        Assert.AreEqual(1, session.WriteCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_MapsEndOfStreamToReadFailed()
    {
        var session = new FakeSession();
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.ReadFailed, response.Result.Status);
        Assert.IsNull(response.Payload);
        Assert.AreEqual(1, session.ReadCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_MapsResponseTimeout()
    {
        var session = new FakeSession();
        session.QueueReadException(new Xb31TransportException(Xb31Status.Timeout));
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.Timeout, response.Result.Status);
        Assert.IsNull(response.Payload);
        Assert.AreEqual(1, session.ReadCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_UsesOneDeadlineTokenAcrossReadsAndAcknowledgement()
    {
        var session = new FakeSession();
        session.QueueReadChunk(Convert.FromHexString(
            "3e000100000009f3121fff0000010101303c"));
        session.QueueReadUntilCancellation();
        var transport = new Xb31Transport(
            new FakePlatform(session),
            Task.Delay,
            TimeSpan.FromMilliseconds(50));

        Xb31TransportResponse response = await transport.RequestAsync(
                Convert.FromHexString("3e00000000000591100fff00b43c"),
                0x92,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(Xb31Status.Timeout, response.Result.Status);
        Assert.IsInstanceOfType<OperationCanceledException>(response.Result.Diagnostic);
        Assert.HasCount(2, session.ReadCancellationTokens);
        Assert.IsTrue(session.ReadCancellationTokens[0].CanBeCanceled);
        Assert.AreEqual(
            session.ReadCancellationTokens[0],
            session.ReadCancellationTokens[1]);
        Assert.HasCount(2, session.WriteCancellationTokens);
        Assert.AreEqual(
            session.ReadCancellationTokens[0],
            session.WriteCancellationTokens[1]);
    }

    [TestMethod]
    public async Task RequestAsync_PreservesReadFailureOverCleanupFailure()
    {
        var session = new FakeSession
        {
            DisposeException = new IOException("cleanup failed")
        };
        session.QueueReadException(new Xb31TransportException(Xb31Status.ReadFailed));
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.ReadFailed, response.Result.Status);
        Assert.IsNull(response.Payload);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_PreservesPayloadWhenCleanupFailsAfterResponse()
    {
        var session = new FakeSession
        {
            DisposeException = new IOException("cleanup failed")
        };
        session.QueueReadChunk(Convert.FromHexString(
            "3e000000000009921002ff0000000000ac3c"));
        var transport = new Xb31Transport(new FakePlatform(session));

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.CleanupFailed, response.Result.Status);
        Assert.IsNotNull(response.Result.Diagnostic);
        CollectionAssert.AreEqual(
            Convert.FromHexString("921002ff0000000000"),
            response.Payload);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task RequestAsync_DoesNotUseCommandSettleDelay()
    {
        var session = new FakeSession();
        session.QueueReadChunk(Convert.FromHexString(
            "3e000000000009921002ff0000000000ac3c"));
        int delayCount = 0;
        var transport = new Xb31Transport(
            new FakePlatform(session),
            (_, _) =>
            {
                delayCount++;
                return Task.CompletedTask;
            });

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.IsTrue(response.Result.IsSuccess);
        Assert.AreEqual(0, delayCount);
    }

    [TestMethod]
    public async Task RequestAsync_DoesNotRetryFailedRequestWrite()
    {
        var session = new FakeSession
        {
            WriteException = new Xb31TransportException(Xb31Status.WriteFailed)
        };
        var platform = new FakePlatform(session);
        var transport = new Xb31Transport(platform);

        Xb31TransportResponse response = await transport.RequestAsync(
            Convert.FromHexString("3e00000000000591100fff00b43c"),
            0x92,
            CancellationToken.None);

        Assert.AreEqual(Xb31Status.WriteFailed, response.Result.Status);
        Assert.IsNull(response.Payload);
        Assert.AreEqual(1, platform.ConnectCount);
        Assert.AreEqual(1, session.WriteCount);
        Assert.AreEqual(0, session.ReadCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public void TimeoutBounds_PreserveProvenDurations()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(10), Xb31Timeouts.Discovery);
        Assert.AreEqual(TimeSpan.FromSeconds(10), Xb31Timeouts.Connection);
        Assert.AreEqual(TimeSpan.FromSeconds(5), Xb31Timeouts.Write);
        Assert.AreEqual(TimeSpan.FromSeconds(5), Xb31Timeouts.Response);
        CollectionAssert.AreEqual(
            new[] { 512 },
            new[] { Xb31Timeouts.ReadChunkSize });
    }

    [TestMethod]
    public void ReadStatuses_AreAppendedAfterExistingValues() =>
        CollectionAssert.AreEqual(
            new[] { 8, 9 },
            new[]
            {
                (int)Xb31Status.ReadFailed,
                (int)Xb31Status.MalformedResponse
            });

    private sealed class FakePlatform : IRfcommPlatform
    {
        private readonly IRfcommSession? _session;
        private readonly Exception? _exception;

        public FakePlatform(IRfcommSession session) => _session = session;

        public FakePlatform(Exception exception) => _exception = exception;

        public int ConnectCount { get; private set; }

        public Task<IRfcommSession> ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectCount++;
            return _exception is null
                ? Task.FromResult(_session!)
                : Task.FromException<IRfcommSession>(_exception);
        }
    }

    private sealed class FakeSession : IRfcommSession
    {
        private readonly Queue<Func<CancellationToken, Task<byte[]>>> _readResults = [];

        public int WriteCount { get; private set; }
        public int ReadCount { get; private set; }
        public int DisposeCount { get; private set; }
        public byte[]? LastFrame { get; private set; }
        public List<byte[]> WrittenFrames { get; } = [];
        public List<int> ReadMaximums { get; } = [];
        public List<CancellationToken> ReadCancellationTokens { get; } = [];
        public List<CancellationToken> WriteCancellationTokens { get; } = [];
        public Exception? WriteException { get; init; }
        public Exception? DisposeException { get; init; }

        public void QueueReadChunk(byte[] chunk)
        {
            byte[] copy = chunk.ToArray();
            _readResults.Enqueue(_ => Task.FromResult(copy.ToArray()));
        }

        public void QueueReadException(Exception exception) =>
            _readResults.Enqueue(_ => Task.FromException<byte[]>(exception));

        public void QueueReadUntilCancellation() =>
            _readResults.Enqueue(async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            });

        public void QueueReadUntilCancellationAsTransportTimeout() =>
            _readResults.Enqueue(async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return [];
                }
                catch (OperationCanceledException exception)
                {
                    throw new Xb31TransportException(Xb31Status.Timeout, exception);
                }
            });

        public Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            WriteCount++;
            LastFrame = frame.ToArray();
            WrittenFrames.Add(frame.ToArray());
            WriteCancellationTokens.Add(cancellationToken);
            return WriteException is null ? Task.CompletedTask : Task.FromException(WriteException);
        }

        public Task<byte[]> ReadAsync(int maximumBytes, CancellationToken cancellationToken)
        {
            ReadCount++;
            ReadMaximums.Add(maximumBytes);
            ReadCancellationTokens.Add(cancellationToken);
            if (_readResults.Count == 0)
                return Task.FromResult<byte[]>([]);

            return _readResults.Dequeue()(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeException is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(DisposeException);
        }
    }
}
