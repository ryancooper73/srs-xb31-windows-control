using Xb31.Core;

namespace Xb31.Control;

public interface IXb31OperationHost
{
    event EventHandler? OperationCompleted;

    bool TryStartLighting(LightingMode mode, out Task operation);
}
