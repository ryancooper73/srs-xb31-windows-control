namespace Xb31.Core.Transport;

internal sealed record Xb31TransportResponse(Xb31Result Result, byte[]? Payload);
