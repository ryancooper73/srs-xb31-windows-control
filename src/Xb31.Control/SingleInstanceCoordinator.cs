namespace Xb31.Control;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    internal const string ProductionInstanceName = @"Local\SRS-XB31.Xb31Control";
    internal const string OfflineStartupTestInstanceName = @"Local\SRS-XB31.Xb31Control.OfflineStartupTest";

    private readonly object _gate = new();
    private readonly EventWaitHandle _activationEvent;
    private readonly Mutex _ownershipMutex;
    private RegisteredWaitHandle? _registeredWait;
    private Action? _activationHandler;
    private bool _disposed;

    private SingleInstanceCoordinator(EventWaitHandle activationEvent, Mutex ownershipMutex)
    {
        _activationEvent = activationEvent;
        _ownershipMutex = ownershipMutex;
    }

    internal static bool TryAcquire(
        string instanceName,
        out SingleInstanceCoordinator? coordinator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);

        var activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"{instanceName}.Activation");

        Mutex? ownershipMutex = null;
        try
        {
            ownershipMutex = new Mutex(true, instanceName, out bool createdNew);
            if (!createdNew)
            {
                activationEvent.Set();
                ownershipMutex.Dispose();
                activationEvent.Dispose();
                coordinator = null;
                return false;
            }

            coordinator = new SingleInstanceCoordinator(activationEvent, ownershipMutex);
            return true;
        }
        catch
        {
            ownershipMutex?.Dispose();
            activationEvent.Dispose();
            throw;
        }
    }

    internal void ListenForActivation(Action activationHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registeredWait is not null)
            {
                throw new InvalidOperationException("The activation listener is already registered.");
            }

            _activationHandler = activationHandler;
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                ActivationSignaled,
                null,
                Timeout.Infinite,
                false);
        }
    }

    public void Dispose()
    {
        RegisteredWaitHandle? registeredWait;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registeredWait = _registeredWait;
            _registeredWait = null;
            _activationHandler = null;
        }

        if (registeredWait is not null)
        {
            using var callbacksCompleted = new ManualResetEvent(false);
            if (registeredWait.Unregister(callbacksCompleted))
            {
                callbacksCompleted.WaitOne();
            }
        }

        _ownershipMutex.ReleaseMutex();
        _ownershipMutex.Dispose();
        _activationEvent.Dispose();
    }

    private void ActivationSignaled(object? state, bool timedOut)
    {
        Action? activationHandler;
        lock (_gate)
        {
            activationHandler = _disposed ? null : _activationHandler;
        }

        activationHandler?.Invoke();
    }
}
