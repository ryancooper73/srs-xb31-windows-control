namespace Xb31.Core.Transport;

internal static class Xb31Timeouts
{
    internal const int ReadChunkSize = 512;
    internal static readonly TimeSpan Discovery = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan Connection = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan Write = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan Response = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan CommandSettle = TimeSpan.FromSeconds(1);
}
