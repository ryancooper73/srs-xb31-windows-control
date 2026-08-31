namespace Xb31.Core.Transport;

internal interface IXb31Transport
{
    Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken);

    Task<Xb31Result> SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);

    Task<Xb31TransportResponse> RequestAsync(
        ReadOnlyMemory<byte> requestFrame,
        byte expectedResponseCommand,
        CancellationToken cancellationToken);

    Task<Xb31TransportBatchResponse> RequestInitializedAsync(
        IReadOnlyList<Xb31TransportRequest> requests,
        CancellationToken cancellationToken);
}
