using Xb31.Core.Protocol;
using Xb31.Core.Transport;

namespace Xb31.Core;

public sealed class Xb31Client : IXb31Client
{
    private readonly IXb31Transport _transport;

    public Xb31Client(Action<string>? report = null)
        : this(new Xb31Transport(new WindowsRfcommPlatform(report)))
    {
    }

    internal Xb31Client(IXb31Transport transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken = default) =>
        _transport.ProbeAsync(cancellationToken);

    public async Task<Xb31StatusResult> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        Xb31TransportBatchResponse response = await _transport.RequestInitializedAsync(
            new Xb31TransportRequest[]
            {
                new([0xF2, 0x11, 0x1F, 0xFF], [0xF3, 0x11]),
                new([0x91, 0x10, 0x0F, 0xFF, 0x00], [0x92, 0x10]),
                new([0xF2, 0x12, 0x1F, 0xFF], [0xF3, 0x12, 0x1F, 0xFF]),
                new([0xF2, 0x12, 0x3F, 0xFF], [0xF3, 0x12, 0x3F, 0xFF])
            },
            cancellationToken).ConfigureAwait(false);

        return new Xb31StatusResult(
            ParseBatchValue(response, 0, payload => Xb31ResponseParser.ParseLightingMode(payload)),
            ParseBatchValue(response, 1, payload => Xb31ResponseParser.ParseSoundMode(payload)),
            ParseBatchValue(response, 2, payload => Xb31ResponseParser.ParseAutoStandby(payload)),
            ParseBatchValue(response, 3, payload => Xb31ResponseParser.ParseBatteryLabel(payload)));
    }

    public Task<Xb31Result> PowerOffAsync(CancellationToken cancellationToken = default) =>
        SendFrameAsync(Xb31Commands.PowerOffFrame, cancellationToken);

    public Task<Xb31Result> SetLightingAsync(
        LightingMode mode,
        CancellationToken cancellationToken = default) =>
        SendFrameAsync(() => Xb31Commands.LightingFrame(mode), cancellationToken);

    public Task<Xb31QueryResult<string>> GetBatteryLabelAsync(
        CancellationToken cancellationToken = default) =>
        QueryInitializedAsync(
            [0xF2, 0x12, 0x3F, 0xFF],
            [0xF3, 0x12, 0x3F, 0xFF],
            payload => Xb31ResponseParser.ParseBatteryLabel(payload),
            cancellationToken);

    public Task<Xb31QueryResult<SoundMode>> GetSoundModeAsync(
        CancellationToken cancellationToken = default) =>
        QueryInitializedAsync(
            [0x91, 0x10, 0x0F, 0xFF, 0x00],
            [0x92, 0x10],
            payload => Xb31ResponseParser.ParseSoundMode(payload),
            cancellationToken);

    public Task<Xb31SetResult<SoundMode>> SetSoundModeAsync(
        SoundMode mode,
        CancellationToken cancellationToken = default) =>
        SetWithReadBackAsync(
            mode,
            () => Xb31Commands.SoundModeFrame(mode),
            GetSoundModeAsync,
            cancellationToken);

    public Task<Xb31QueryResult<bool>> GetAutoStandbyAsync(
        CancellationToken cancellationToken = default) =>
        QueryInitializedAsync(
            [0xF2, 0x12, 0x1F, 0xFF],
            [0xF3, 0x12, 0x1F, 0xFF],
            payload => Xb31ResponseParser.ParseAutoStandby(payload),
            cancellationToken);

    public Task<Xb31SetResult<bool>> SetAutoStandbyAsync(
        bool isOn,
        CancellationToken cancellationToken = default) =>
        SetWithReadBackAsync(
            isOn,
            () => Xb31Commands.AutoStandbyFrame(isOn),
            GetAutoStandbyAsync,
            cancellationToken);

    private async Task<Xb31QueryResult<T>> QueryInitializedAsync<T>(
        byte[] requestPayload,
        byte[] expectedResponsePrefix,
        Func<byte[], T> parse,
        CancellationToken cancellationToken)
    {
        Xb31TransportBatchResponse response = await _transport.RequestInitializedAsync(
            [new Xb31TransportRequest(requestPayload, expectedResponsePrefix)],
            cancellationToken).ConfigureAwait(false);
        return ParseBatchValue(response, 0, parse);
    }

    private async Task<Xb31QueryResult<T>> QueryAsync<T>(
        byte[] requestFrame,
        byte expectedResponseCommand,
        Func<byte[], T> parse,
        CancellationToken cancellationToken)
    {
        Xb31TransportResponse response = await _transport.RequestAsync(
            requestFrame,
            expectedResponseCommand,
            cancellationToken);

        if (response.Payload is null)
        {
            return new Xb31QueryResult<T>(
                response.Result.Status,
                false,
                default!,
                response.Result.Diagnostic);
        }

        try
        {
            return new Xb31QueryResult<T>(
                response.Result.Status,
                true,
                parse(response.Payload),
                response.Result.Diagnostic);
        }
        catch (FormatException exception)
        {
            return new Xb31QueryResult<T>(
                Xb31Status.MalformedResponse,
                false,
                default!,
                exception);
        }
    }

    private static Xb31QueryResult<T> ParseBatchValue<T>(
        Xb31TransportBatchResponse response,
        int index,
        Func<byte[], T> parse)
    {
        byte[]? payload = index < response.Payloads.Count ? response.Payloads[index] : null;
        if (payload is null)
        {
            return new Xb31QueryResult<T>(
                response.Result.Status,
                false,
                default!,
                response.Result.Diagnostic);
        }

        try
        {
            Xb31Status status = response.Result.Status == Xb31Status.CleanupFailed
                ? Xb31Status.CleanupFailed
                : Xb31Status.Success;
            return new Xb31QueryResult<T>(
                status,
                true,
                parse(payload),
                status == Xb31Status.CleanupFailed ? response.Result.Diagnostic : null);
        }
        catch (FormatException exception)
        {
            return new Xb31QueryResult<T>(
                Xb31Status.MalformedResponse,
                false,
                default!,
                exception);
        }
    }

    private async Task<Xb31SetResult<T>> SetWithReadBackAsync<T>(
        T requestedValue,
        Func<byte[]> createFrame,
        Func<CancellationToken, Task<Xb31QueryResult<T>>> readBack,
        CancellationToken cancellationToken)
    {
        Xb31Result sendResult = await SendFrameAsync(createFrame, cancellationToken);
        Xb31QueryResult<T>? value = sendResult.Status is Xb31Status.Success or Xb31Status.CleanupFailed
            ? await readBack(cancellationToken).ConfigureAwait(false)
            : null;
        return new Xb31SetResult<T>(requestedValue, sendResult, value);
    }

    private Task<Xb31Result> SendFrameAsync(
        Func<byte[]> createFrame,
        CancellationToken cancellationToken)
    {
        byte[] frame;
        try
        {
            frame = createFrame();
        }
        catch (Exception exception)
        {
            return Task.FromResult(new Xb31Result(Xb31Status.MalformedCommand, exception));
        }

        return _transport.SendAsync(frame, cancellationToken);
    }
}
