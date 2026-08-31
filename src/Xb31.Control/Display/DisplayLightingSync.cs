using Xb31.Core;

namespace Xb31.Control;

internal sealed class DisplayLightingSync : IDisposable
{
    private readonly object _gate = new();
    private readonly IXb31OperationHost _operations;
    private bool _enabled;
    private bool _disposed;
    private DisplayState? _effectiveState;
    private AutomaticOperation? _automaticInFlight;
    private LightingMode? _pending;

    internal DisplayLightingSync(IXb31OperationHost operations, bool enabled)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _enabled = enabled;
        _operations.OperationCompleted += OnOperationCompleted;
    }

    internal bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
        set
        {
            lock (_gate)
            {
                _enabled = value;
                if (!value)
                {
                    _pending = null;
                }
            }
        }
    }

    internal DisplayState? EffectiveState
    {
        get
        {
            lock (_gate)
            {
                return _effectiveState;
            }
        }
    }

    internal void Observe(DisplayState state)
    {
        if (state == DisplayState.Dim)
        {
            return;
        }

        lock (_gate)
        {
            if (_effectiveState is null)
            {
                _effectiveState = state;
                return;
            }

            if (_effectiveState == state)
            {
                return;
            }

            _effectiveState = state;
            if (!_enabled)
            {
                return;
            }

            LightingMode desired = state switch
            {
                DisplayState.Off => LightingMode.LightOff,
                DisplayState.On => LightingMode.Chill,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            };

            if (_automaticInFlight?.Mode == desired)
            {
                _pending = null;
                return;
            }

            if (_automaticInFlight is not null)
            {
                _pending = desired;
                return;
            }

            TryStartLocked(desired);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending = null;
        }

        _operations.OperationCompleted -= OnOperationCompleted;
    }

    private void OnOperationCompleted(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_automaticInFlight is not null)
            {
                if (_automaticInFlight.Operation?.IsCompleted == true)
                {
                    CompleteAutomaticLocked(_automaticInFlight);
                }

                return;
            }

            TryStartPendingLocked();
        }
    }

    private void TryStartLocked(LightingMode desired)
    {
        var automatic = new AutomaticOperation(desired);
        _automaticInFlight = automatic;
        _pending = null;
        if (_operations.TryStartLighting(desired, out Task operation))
        {
            automatic.Operation = operation;
            _ = CompleteAutomaticAsync(automatic, operation);
            return;
        }

        _automaticInFlight = null;
        _pending = desired;
    }

    private async Task CompleteAutomaticAsync(AutomaticOperation automatic, Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
            // The view model reports operation failures; synchronization never retries them.
        }

        lock (_gate)
        {
            CompleteAutomaticLocked(automatic);
        }
    }

    private void CompleteAutomaticLocked(AutomaticOperation automatic)
    {
        if (_disposed || !ReferenceEquals(_automaticInFlight, automatic))
        {
            return;
        }

        _automaticInFlight = null;
        TryStartPendingLocked();
    }

    private void TryStartPendingLocked()
    {
        if (!_enabled || _pending is not LightingMode pending)
        {
            _pending = null;
            return;
        }

        _pending = null;
        TryStartLocked(pending);
    }

    private sealed record AutomaticOperation(LightingMode Mode)
    {
        internal Task? Operation { get; set; }
    }
}
