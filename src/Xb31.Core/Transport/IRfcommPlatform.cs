namespace Xb31.Core.Transport;

internal interface IRfcommPlatform
{
    Task<IRfcommSession> ConnectAsync(CancellationToken cancellationToken);
}
