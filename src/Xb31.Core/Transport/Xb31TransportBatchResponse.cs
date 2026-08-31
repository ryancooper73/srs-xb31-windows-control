namespace Xb31.Core.Transport;

internal sealed record Xb31TransportBatchResponse(
    Xb31Result Result,
    IReadOnlyList<byte[]?> Payloads);
