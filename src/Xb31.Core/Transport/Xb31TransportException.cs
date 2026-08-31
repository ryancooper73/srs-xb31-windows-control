namespace Xb31.Core.Transport;

internal sealed class Xb31TransportException : Exception
{
    internal Xb31TransportException(Xb31Status status)
        : base($"XB31 transport failed with status {status}.") =>
        Status = status;

    internal Xb31TransportException(Xb31Status status, Exception innerException)
        : base($"XB31 transport failed with status {status}.", innerException) =>
        Status = status;

    internal Xb31Status Status { get; }
}
