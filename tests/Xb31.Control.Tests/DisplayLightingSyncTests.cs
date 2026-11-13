using Xb31.Control;
using Xb31.Core;

namespace Xb31.Control.Tests;

[TestClass]
public sealed class DisplayLightingSyncTests
{
    [TestMethod]
    public void Observe_BaselineDimAndDuplicates_SendOnlyGenuineTransition()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: true);

        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Dim);
        sync.Observe(DisplayState.On);
        Assert.IsEmpty(host.StartedModes);

        sync.Observe(DisplayState.Off);
        CollectionAssert.AreEqual(new[] { LightingMode.LightOff }, host.StartedModes);
        sync.Observe(DisplayState.Off);
        Assert.HasCount(1, host.StartedModes);
    }

    [TestMethod]
    public void Observe_DimPreservesEffectiveOff_SoOnSendsChill()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: true);

        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        host.CompleteCurrent();
        sync.Observe(DisplayState.Dim);
        sync.Observe(DisplayState.On);

        CollectionAssert.AreEqual(
            new[] { LightingMode.LightOff, LightingMode.Chill },
            host.StartedModes);
    }

    [TestMethod]
    public void Observe_WhileManualOperationIsBusy_KeepsOnlyLatestPendingTarget()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: true);

        sync.Observe(DisplayState.On);
        host.BeginManualOperation();
        sync.Observe(DisplayState.Off);
        sync.Observe(DisplayState.On);
        Assert.IsEmpty(host.StartedModes);

        host.CompleteCurrent();

        CollectionAssert.AreEqual(new[] { LightingMode.Chill }, host.StartedModes);
    }

    [TestMethod]
    public void Observe_ReturningToInFlightTarget_ClearsOppositePendingTarget()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: true);

        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        host.CompleteCurrent();

        CollectionAssert.AreEqual(new[] { LightingMode.LightOff }, host.StartedModes);
    }

    [TestMethod]
    public void Observe_InitialDim_DoesNotEstablishEffectiveStateOrSend()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: true);

        sync.Observe(DisplayState.Dim);

        Assert.IsNull(sync.EffectiveState);
        Assert.IsEmpty(host.StartedModes);
    }

    [TestMethod]
    public void Observe_WhileDisabled_UpdatesBaselineWithoutSendingOrReplayingOnEnable()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: false);

        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        sync.Observe(DisplayState.On);
        sync.Enabled = true;

        Assert.AreEqual(DisplayState.On, sync.EffectiveState);
        Assert.IsEmpty(host.StartedModes);

        sync.Observe(DisplayState.Off);
        CollectionAssert.AreEqual(new[] { LightingMode.LightOff }, host.StartedModes);
    }

    [TestMethod]
    public void Disable_ClearsPendingTarget()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: true);
        sync.Observe(DisplayState.On);
        host.BeginManualOperation();
        sync.Observe(DisplayState.Off);

        sync.Enabled = false;
        host.CompleteCurrent();

        Assert.IsEmpty(host.StartedModes);
    }

    [TestMethod]
    public async Task AutomaticTransientFailure_RetriesTheCurrentDesiredTargetOnce()
    {
        var host = new FakeOperationHost();
        var delay = new ControlledDelay();
        using var sync = new DisplayLightingSync(host, enabled: true, delayAsync: delay.WaitAsync);
        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);

        host.CompleteCurrent(new Xb31Result(Xb31Status.Unavailable));
        await delay.WaitUntilRequestedAsync();
        delay.Release();
        await Task.Yield();

        CollectionAssert.AreEqual(
            new[] { LightingMode.LightOff, LightingMode.LightOff },
            host.StartedModes);
    }

    [TestMethod]
    public async Task AutomaticRetry_AChangedDisplayStateInvalidatesTheStaleRetry()
    {
        var host = new FakeOperationHost();
        var delay = new ControlledDelay();
        using var sync = new DisplayLightingSync(host, enabled: true, delayAsync: delay.WaitAsync);
        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        host.CompleteCurrent(new Xb31Result(Xb31Status.Timeout));
        await delay.WaitUntilRequestedAsync();

        sync.Observe(DisplayState.On);
        delay.Release();
        await Task.Yield();

        CollectionAssert.AreEqual(
            new[] { LightingMode.LightOff, LightingMode.Chill },
            host.StartedModes);
    }

    [TestMethod]
    public async Task AutomaticRetry_DisablingSyncBeforeDelayPreventsRetry()
    {
        var host = new FakeOperationHost();
        var delay = new ControlledDelay();
        using var sync = new DisplayLightingSync(host, enabled: true, delayAsync: delay.WaitAsync);
        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        host.CompleteCurrent(new Xb31Result(Xb31Status.ConnectionFailed));
        await delay.WaitUntilRequestedAsync();

        sync.Enabled = false;
        delay.Release();
        await Task.Yield();

        CollectionAssert.AreEqual(new[] { LightingMode.LightOff }, host.StartedModes);
    }

    [TestMethod]
    public async Task AutomaticRetry_DisposalBeforeDelayPreventsRetry()
    {
        var host = new FakeOperationHost();
        var delay = new ControlledDelay();
        var sync = new DisplayLightingSync(host, enabled: true, delayAsync: delay.WaitAsync);
        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        host.CompleteCurrent(new Xb31Result(Xb31Status.Unavailable));
        await delay.WaitUntilRequestedAsync();

        sync.Dispose();
        delay.Release();
        await Task.Yield();

        CollectionAssert.AreEqual(new[] { LightingMode.LightOff }, host.StartedModes);
    }

    [TestMethod]
    public void AutomaticRetry_NonTransientFailureDoesNotScheduleARetry()
    {
        var host = new FakeOperationHost();
        var delay = new ControlledDelay();
        using var sync = new DisplayLightingSync(host, enabled: true, delayAsync: delay.WaitAsync);
        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        host.CompleteCurrent(new Xb31Result(Xb31Status.WriteFailed));

        Assert.IsFalse(delay.WasRequested);
        CollectionAssert.AreEqual(new[] { LightingMode.LightOff }, host.StartedModes);
    }

    [TestMethod]
    public void SynchronousOperationCompletion_DoesNotLeaveAutomaticTargetInFlight()
    {
        var host = new FakeOperationHost { CompleteStartsSynchronously = true };
        using var sync = new DisplayLightingSync(host, enabled: true);
        sync.Observe(DisplayState.On);

        sync.Observe(DisplayState.Off);
        sync.Observe(DisplayState.On);

        CollectionAssert.AreEqual(
            new[] { LightingMode.LightOff, LightingMode.Chill },
            host.StartedModes);
    }

    [TestMethod]
    public void DelayedPriorCompletion_DoesNotClearNewAutomaticTargetOrCauseDuplicate()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: true);
        sync.Observe(DisplayState.Off);
        host.BeginManualOperation();
        sync.Observe(DisplayState.On);
        host.ReleaseCurrentWithoutSignalingCompletion();

        sync.Observe(DisplayState.Off);
        host.SignalCompletion();
        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);
        host.CompleteCurrent();

        CollectionAssert.AreEqual(new[] { LightingMode.LightOff }, host.StartedModes);
    }

    [TestMethod]
    public void Dispose_UnsubscribesFromOperationCompletion()
    {
        var host = new FakeOperationHost();
        var sync = new DisplayLightingSync(host, enabled: true);
        Assert.AreEqual(1, host.CompletionSubscribers);

        sync.Dispose();

        Assert.AreEqual(0, host.CompletionSubscribers);
    }

    private sealed class FakeOperationHost : IXb31OperationHost
    {
        private EventHandler? _operationCompleted;
        private TaskCompletionSource<Xb31Result>? _currentOperation;

        public event EventHandler? OperationCompleted
        {
            add => _operationCompleted += value;
            remove => _operationCompleted -= value;
        }

        public bool Busy { get; private set; }

        public bool CompleteStartsSynchronously { get; init; }

        public int CompletionSubscribers => _operationCompleted?.GetInvocationList().Length ?? 0;

        public List<LightingMode> StartedModes { get; } = [];

        public bool TryStartLighting(LightingMode mode, out Task<Xb31Result> operation)
        {
            if (Busy)
            {
                operation = Task.FromResult(Xb31Result.Success);
                return false;
            }

            Busy = true;
            StartedModes.Add(mode);
            _currentOperation = new TaskCompletionSource<Xb31Result>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            operation = _currentOperation.Task;

            if (CompleteStartsSynchronously)
            {
                CompleteCurrent();
            }

            return true;
        }

        public void BeginManualOperation()
        {
            Assert.IsFalse(Busy);
            Busy = true;
            _currentOperation = new TaskCompletionSource<Xb31Result>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void CompleteCurrent(Xb31Result? result = null)
        {
            ReleaseCurrentWithoutSignalingCompletion(result);
            SignalCompletion();
        }

        public void ReleaseCurrentWithoutSignalingCompletion(Xb31Result? result = null)
        {
            Assert.IsTrue(Busy);
            Busy = false;
            _currentOperation!.SetResult(result ?? Xb31Result.Success);
            _currentOperation = null;
        }

        public void SignalCompletion() => _operationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ControlledDelay
    {
        private readonly TaskCompletionSource<object?> _requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasRequested => _requested.Task.IsCompleted;

        public Task WaitAsync(TimeSpan _)
        {
            _requested.TrySetResult(null);
            return _release.Task;
        }

        public Task WaitUntilRequestedAsync() => _requested.Task;

        public void Release() => _release.TrySetResult(null);
    }
}
