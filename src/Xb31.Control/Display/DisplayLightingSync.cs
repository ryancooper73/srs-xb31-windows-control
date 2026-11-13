using Xb31.Core;

namespace Xb31.Control;

internal sealed class DisplayLightingSync : IDisposable
{
    private readonly object _gate = new();
    private readonly IXb31OperationHost _operations;
    private readonly Func<TimeSpan, Task> _delayAsync;
    private readonly Action<string, string?> _trace;
    private bool _enabled;
    private bool _disposed;
    private DisplayState? _effectiveState;
    private AutomaticOperation? _automaticInFlight;
    private LightingMode? _pending;
    private int _stateGeneration;

    internal DisplayLightingSync(
        IXb31OperationHost operations,
        bool enabled,
        Func<TimeSpan, Task>? delayAsync = null,
        Action<string, string?>? trace = null)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _delayAsync = delayAsync ?? Task.Delay;
        _trace = trace ?? ((_, _) => { });
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
                    _stateGeneration++;
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
            _stateGeneration++;
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
            _stateGeneration++;
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
                return;
            }

            TryStartPendingLocked();
        }
    }

    private void TryStartLocked(LightingMode desired, int retryCount = 0)
    {
        _trace("DisplaySyncStart", $"mode={desired}; attempt={retryCount + 1}");
        var automatic = new AutomaticOperation(desired, retryCount);
        _automaticInFlight = automatic;
        _pending = null;
        if (_operations.TryStartLighting(desired, out Task<Xb31Result> operation))
        {
            automatic.Operation = operation;
            _ = CompleteAutomaticAsync(automatic, operation);
            return;
        }

        _automaticInFlight = null;
        _pending = desired;
        _trace("DisplaySyncPending", $"mode={desired}");
    }

    private async Task CompleteAutomaticAsync(AutomaticOperation automatic, Task<Xb31Result> operation)
    {
        Xb31Result? result = null;
        try
        {
            result = await operation.ConfigureAwait(false);
        }
        catch
        {
            // The view model reports unexpected operation exceptions.
        }

        lock (_gate)
        {
            CompleteAutomaticLocked(automatic, result);
        }
    }

    private void CompleteAutomaticLocked(AutomaticOperation automatic, Xb31Result? result)
    {
        if (_disposed || !ReferenceEquals(_automaticInFlight, automatic))
        {
            return;
        }

        _automaticInFlight = null;
        if (_pending is not null)
        {
            TryStartPendingLocked();
            return;
        }

        if (result is { } completed &&
            automatic.RetryCount == 0 &&
            IsTransient(completed.Status) &&
            DesiredFor(_effectiveState) == automatic.Mode)
        {
            _trace("DisplaySyncRetryScheduled", $"mode={automatic.Mode}; status={completed.Status}");
            ScheduleRetryLocked(automatic.Mode, _stateGeneration);
            return;
        }

        _trace(
            "DisplaySyncResult",
            $"mode={automatic.Mode}; status={result?.Status.ToString() ?? "exception"}; attempt={automatic.RetryCount + 1}");

        TryStartPendingLocked();
    }

    private void ScheduleRetryLocked(LightingMode desired, int generation)
    {
        _ = RetryAutomaticAsync(desired, generation);
    }

    private async Task RetryAutomaticAsync(LightingMode desired, int generation)
    {
        await _delayAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

        lock (_gate)
        {
            if (_disposed ||
                !_enabled ||
                generation != _stateGeneration ||
                DesiredFor(_effectiveState) != desired ||
                _automaticInFlight is not null)
            {
                _trace("DisplaySyncRetryCancelled", $"mode={desired}");
                return;
            }

            _trace("DisplaySyncRetryStarting", $"mode={desired}");
            TryStartLocked(desired, retryCount: 1);
        }
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

    private static LightingMode? DesiredFor(DisplayState? state) => state switch
    {
        DisplayState.Off => LightingMode.LightOff,
        DisplayState.On => LightingMode.Chill,
        _ => null
    };

    private static bool IsTransient(Xb31Status status) => status is
        Xb31Status.Unavailable or Xb31Status.ConnectionFailed or Xb31Status.Timeout;

    private sealed record AutomaticOperation(LightingMode Mode, int RetryCount)
    {
        internal Task<Xb31Result>? Operation { get; set; }
    }
}
