using Xb31.Core.Transport;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class Xb31ClientTests
{
    [TestMethod]
    public async Task GetStatusAsync_UsesOneInitializedBatchAndParsesAllCapturedValues()
    {
        var transport = new FakeTransport
        {
            BatchResponse = new Xb31TransportBatchResponse(
                Xb31Result.Success,
                new byte[]?[]
                {
                    Convert.FromHexString("f31112ff00000000"),
                    Convert.FromHexString("921000ff0000000000"),
                    Convert.FromHexString("f3121fff0000010101"),
                    Convert.FromHexString("f3123fff0001210d46756c6c792063686172676564")
                })
        };
        var client = new Xb31Client(transport);

        Xb31StatusResult result = await client.GetStatusAsync();

        Assert.AreEqual(1, transport.BatchCalls);
        Assert.HasCount(4, transport.LastBatchRequests!);
        CollectionAssert.AreEqual(
            new[] { "f2111fff", "91100fff00", "f2121fff", "f2123fff" },
            transport.LastBatchRequests!.Select(request => Hex(request.Payload)).ToArray());
        CollectionAssert.AreEqual(
            new[] { "f311", "9210", "f3121fff", "f3123fff" },
            transport.LastBatchRequests!.Select(request => Hex(request.ExpectedResponsePrefix)).ToArray());
        Assert.AreEqual(LightingMode.Chill, result.Lighting.Value);
        Assert.AreEqual(SoundMode.Standard, result.Sound.Value);
        Assert.IsTrue(result.AutoStandby.Value);
        Assert.AreEqual("Fully charged", result.BatteryLabel.Value);
    }

    [TestMethod]
    public async Task GetStatusAsync_PreservesSuccessForCompletedValuesWhenLaterRequestFails()
    {
        var diagnostic = new TimeoutException("sound response timed out");
        var transport = new FakeTransport
        {
            BatchResponse = new Xb31TransportBatchResponse(
                new Xb31Result(Xb31Status.Timeout, diagnostic),
                new byte[]?[]
                {
                    Convert.FromHexString("f31112ff00000000"),
                    null,
                    null,
                    null
                })
        };
        var client = new Xb31Client(transport);

        Xb31StatusResult result = await client.GetStatusAsync();

        Assert.AreEqual(Xb31Status.Success, result.Lighting.Status);
        Assert.IsTrue(result.Lighting.HasValue);
        Assert.AreEqual(LightingMode.Chill, result.Lighting.Value);
        Assert.IsNull(result.Lighting.Diagnostic);
        Assert.AreEqual(Xb31Status.Timeout, result.Sound.Status);
        Assert.IsFalse(result.Sound.HasValue);
        Assert.AreSame(diagnostic, result.Sound.Diagnostic);
        Assert.AreEqual(Xb31Status.Timeout, result.AutoStandby.Status);
        Assert.AreEqual(Xb31Status.Timeout, result.BatteryLabel.Status);
    }

    [TestMethod]
    public async Task ProbeAsync_ForwardsExactlyOnce()
    {
        var transport = new FakeTransport();
        var client = new Xb31Client(transport);

        Xb31Result result = await client.ProbeAsync();

        Assert.AreSame(Xb31Result.Success, result);
        Assert.AreEqual(1, transport.ProbeCalls);
        Assert.AreEqual(0, transport.SendCalls);
    }

    [TestMethod]
    public async Task PowerOffAsync_SendsExactGeneratedFrameOnce()
    {
        var transport = new FakeTransport();
        var client = new Xb31Client(transport);

        Xb31Result result = await client.PowerOffAsync();

        Assert.AreSame(Xb31Result.Success, result);
        Assert.AreEqual(1, transport.SendCalls);
        CollectionAssert.AreEqual(
            Convert.FromHexString("3e0000000000053000000f00443c"),
            transport.LastFrame);
    }

    [TestMethod]
    public async Task SetLightingAsync_SendsExactGeneratedFrameOnce()
    {
        var transport = new FakeTransport();
        var client = new Xb31Client(transport);

        Xb31Result result = await client.SetLightingAsync(LightingMode.Chill);

        Assert.AreSame(Xb31Result.Success, result);
        Assert.AreEqual(1, transport.SendCalls);
        CollectionAssert.AreEqual(
            Convert.FromHexString("3e000000000006f41112ff00001c3c"),
            transport.LastFrame);
    }

    [TestMethod]
    public async Task SetLightingAsync_InvalidModeReturnsMalformedCommandWithoutSending()
    {
        var transport = new FakeTransport();
        var client = new Xb31Client(transport);

        Xb31Result result = await client.SetLightingAsync((LightingMode)0xFF);

        Assert.AreEqual(Xb31Status.MalformedCommand, result.Status);
        Assert.IsNotNull(result.Diagnostic);
        Assert.AreEqual(0, transport.SendCalls);
    }

    [TestMethod]
    public async Task GetBatteryLabelAsync_RequestsExactFrameAndParsesLocalizedLabel()
    {
        var transport = new FakeTransport
        {
            BatchResponse = SuccessfulBatch("f3123fff000000074368617267C3A9")
        };
        var client = new Xb31Client(transport);

        Xb31QueryResult<string> result = await client.GetBatteryLabelAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.HasValue);
        Assert.AreEqual("Chargé", result.Value);
        Assert.AreEqual(1, transport.BatchCalls);
        CollectionAssert.AreEqual(
            Convert.FromHexString("f2123fff"),
            transport.LastBatchRequests!.Single().Payload);
        CollectionAssert.AreEqual(
            Convert.FromHexString("f3123fff"),
            transport.LastBatchRequests!.Single().ExpectedResponsePrefix);
    }

    [TestMethod]
    public async Task GetSoundModeAsync_RequestsExactFrameAndParsesTypedValue()
    {
        var transport = new FakeTransport
        {
            BatchResponse = SuccessfulBatch("921002ff")
        };
        var client = new Xb31Client(transport);

        Xb31QueryResult<SoundMode> result = await client.GetSoundModeAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.HasValue);
        Assert.AreEqual(SoundMode.LiveSound, result.Value);
        Assert.AreEqual(1, transport.BatchCalls);
        CollectionAssert.AreEqual(
            Convert.FromHexString("91100fff00"),
            transport.LastBatchRequests!.Single().Payload);
        CollectionAssert.AreEqual(
            Convert.FromHexString("9210"),
            transport.LastBatchRequests!.Single().ExpectedResponsePrefix);
    }

    [TestMethod]
    public async Task GetAutoStandbyAsync_RequestsExactFrameAndParsesTypedValue()
    {
        var transport = new FakeTransport
        {
            BatchResponse = SuccessfulBatch("f3121fff0000010101")
        };
        var client = new Xb31Client(transport);

        Xb31QueryResult<bool> result = await client.GetAutoStandbyAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.HasValue);
        Assert.IsTrue(result.Value);
        Assert.AreEqual(1, transport.BatchCalls);
        CollectionAssert.AreEqual(
            Convert.FromHexString("f2121fff"),
            transport.LastBatchRequests!.Single().Payload);
        CollectionAssert.AreEqual(
            Convert.FromHexString("f3121fff"),
            transport.LastBatchRequests!.Single().ExpectedResponsePrefix);
    }

    [TestMethod]
    public async Task GetSoundModeAsync_MalformedPayloadMapsToMalformedResponse()
    {
        var transport = new FakeTransport
        {
            BatchResponse = SuccessfulBatch("921003ff")
        };
        var client = new Xb31Client(transport);

        Xb31QueryResult<SoundMode> result = await client.GetSoundModeAsync();

        Assert.AreEqual(Xb31Status.MalformedResponse, result.Status);
        Assert.IsFalse(result.HasValue);
        Assert.IsInstanceOfType<FormatException>(result.Diagnostic);
    }

    [TestMethod]
    public async Task GetBatteryLabelAsync_CleanupFailureCarriesParsedValueAndDiagnostic()
    {
        var diagnostic = new InvalidOperationException("cleanup failed");
        var transport = new FakeTransport
        {
            BatchResponse = new Xb31TransportBatchResponse(
                new Xb31Result(Xb31Status.CleanupFailed, diagnostic),
                new byte[]?[] { Convert.FromHexString("f3123fff000000024f4b") })
        };
        var client = new Xb31Client(transport);

        Xb31QueryResult<string> result = await client.GetBatteryLabelAsync();

        Assert.AreEqual(Xb31Status.CleanupFailed, result.Status);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.HasValue);
        Assert.AreEqual("OK", result.Value);
        Assert.AreSame(diagnostic, result.Diagnostic);
    }

    [TestMethod]
    public async Task SetSoundModeAsync_SendsExactFrameAndConfirmsInitializedReadBack()
    {
        var transport = new FakeTransport
        {
            BatchResponse = SuccessfulBatch("921001ff0000000000")
        };
        var client = new Xb31Client(transport);

        Xb31SetResult<SoundMode> result = await client.SetSoundModeAsync(SoundMode.ExtraBass);

        Assert.AreEqual(1, transport.SendCalls);
        CollectionAssert.AreEqual(
            Convert.FromHexString("3e000000000006931001ff0000a93c"),
            transport.LastFrame);
        Assert.AreEqual(1, transport.BatchCalls);
        CollectionAssert.AreEqual(new[] { "Send", "Batch" }, transport.Calls);
        Assert.AreEqual(SoundMode.ExtraBass, result.RequestedValue);
        Assert.IsTrue(result.WasSent);
        Assert.IsTrue(result.IsConfirmed);
        Assert.AreEqual(SoundMode.ExtraBass, result.ReadBack!.Value);
    }

    [TestMethod]
    [DataRow(false, "3e000000000007f4121fff0101002d3c")]
    [DataRow(true, "3e000000000007f4121fff0101012e3c")]
    public async Task SetAutoStandbyAsync_SendsExactFrameAndConfirmsInitializedReadBack(
        bool isOn,
        string expectedSettingFrame)
    {
        var transport = new FakeTransport
        {
            BatchResponse = SuccessfulBatch(isOn
                ? "f3121fff0000010101"
                : "f3121fff0000010100")
        };
        var client = new Xb31Client(transport);

        Xb31SetResult<bool> result = await client.SetAutoStandbyAsync(isOn);

        Assert.AreEqual(1, transport.SendCalls);
        CollectionAssert.AreEqual(
            Convert.FromHexString(expectedSettingFrame),
            transport.LastFrame);
        Assert.AreEqual(1, transport.BatchCalls);
        Assert.AreEqual(isOn, result.RequestedValue);
        Assert.IsTrue(result.WasSent);
        Assert.IsTrue(result.IsConfirmed);
        Assert.AreEqual(isOn, result.ReadBack!.Value);
    }

    [TestMethod]
    public async Task SetSoundModeAsync_WriteFailureSkipsReadBack()
    {
        var diagnostic = new InvalidOperationException("write failed");
        var sendResult = new Xb31Result(Xb31Status.WriteFailed, diagnostic);
        var transport = new FakeTransport { Result = sendResult };
        var client = new Xb31Client(transport);

        Xb31SetResult<SoundMode> result = await client.SetSoundModeAsync(SoundMode.Standard);

        Assert.AreSame(sendResult, result.SendResult);
        Assert.IsFalse(result.WasSent);
        Assert.IsFalse(result.IsConfirmed);
        Assert.IsNull(result.ReadBack);
        Assert.AreEqual(1, transport.SendCalls);
        Assert.AreEqual(0, transport.BatchCalls);
    }

    [TestMethod]
    public async Task SetSoundModeAsync_CleanupFailureAfterSendStillConfirmsReadBack()
    {
        var diagnostic = new InvalidOperationException("cleanup failed");
        var transport = new FakeTransport
        {
            Result = new Xb31Result(Xb31Status.CleanupFailed, diagnostic),
            BatchResponse = SuccessfulBatch("921002ff0000000000")
        };
        var client = new Xb31Client(transport);

        Xb31SetResult<SoundMode> result = await client.SetSoundModeAsync(SoundMode.LiveSound);

        Assert.IsTrue(result.WasSent);
        Assert.IsTrue(result.IsConfirmed);
        Assert.AreEqual(SoundMode.LiveSound, result.ReadBack!.Value);
        Assert.AreEqual(Xb31Status.CleanupFailed, result.SendResult.Status);
        Assert.AreSame(diagnostic, result.SendResult.Diagnostic);
        Assert.AreEqual(1, transport.SendCalls);
        Assert.AreEqual(1, transport.BatchCalls);
    }

    [TestMethod]
    public async Task SetSoundModeAsync_InvalidModeReturnsMalformedCommandWithoutTransportCalls()
    {
        var transport = new FakeTransport();
        var client = new Xb31Client(transport);

        Xb31SetResult<SoundMode> result = await client.SetSoundModeAsync((SoundMode)0xFF);

        Assert.AreEqual(Xb31Status.MalformedCommand, result.SendResult.Status);
        Assert.IsNotNull(result.SendResult.Diagnostic);
        Assert.IsFalse(result.WasSent);
        Assert.IsFalse(result.IsConfirmed);
        Assert.IsNull(result.ReadBack);
        Assert.AreEqual(0, transport.SendCalls);
        Assert.AreEqual(0, transport.BatchCalls);
    }

    private static Xb31TransportResponse SuccessfulResponse(string payload) =>
        new(Xb31Result.Success, Convert.FromHexString(payload));

    private static Xb31TransportBatchResponse SuccessfulBatch(string payload) =>
        new(Xb31Result.Success, new byte[]?[] { Convert.FromHexString(payload) });

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private sealed class FakeTransport : IXb31Transport
    {
        public int ProbeCalls { get; private set; }
        public int SendCalls { get; private set; }
        public int RequestCalls { get; private set; }
        public int BatchCalls { get; private set; }
        public byte[]? LastFrame { get; private set; }
        public byte[]? LastRequestFrame { get; private set; }
        public byte LastExpectedResponseCommand { get; private set; }
        public IReadOnlyList<Xb31TransportRequest>? LastBatchRequests { get; private set; }
        public string[] Calls => _calls.ToArray();
        public Xb31Result Result { get; init; } = Xb31Result.Success;
        public Xb31TransportResponse RequestResponse { get; init; } =
            SuccessfulResponse("921000ff");
        public Xb31TransportBatchResponse BatchResponse { get; init; } =
            new(Xb31Result.Success, []);

        private readonly List<string> _calls = [];

        public Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return Task.FromResult(Result);
        }

        public Task<Xb31Result> SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            SendCalls++;
            LastFrame = frame.ToArray();
            _calls.Add("Send");
            return Task.FromResult(Result);
        }

        public Task<Xb31TransportResponse> RequestAsync(
            ReadOnlyMemory<byte> requestFrame,
            byte expectedResponseCommand,
            CancellationToken cancellationToken)
        {
            RequestCalls++;
            LastRequestFrame = requestFrame.ToArray();
            LastExpectedResponseCommand = expectedResponseCommand;
            _calls.Add("Request");
            return Task.FromResult(RequestResponse);
        }

        public Task<Xb31TransportBatchResponse> RequestInitializedAsync(
            IReadOnlyList<Xb31TransportRequest> requests,
            CancellationToken cancellationToken)
        {
            BatchCalls++;
            LastBatchRequests = requests;
            _calls.Add("Batch");
            return Task.FromResult(BatchResponse);
        }
    }
}
