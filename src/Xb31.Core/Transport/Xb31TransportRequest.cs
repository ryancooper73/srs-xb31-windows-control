namespace Xb31.Core.Transport;

internal sealed record Xb31TransportRequest(
    byte[] Payload,
    byte[] ExpectedResponsePrefix);
