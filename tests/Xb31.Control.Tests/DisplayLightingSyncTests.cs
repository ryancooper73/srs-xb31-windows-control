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
    public void OperationCompleted_AfterFailedAutomaticAttempt_DoesNotRetry()
    {
        var host = new FakeOperationHost();
        using var sync = new DisplayLightingSync(host, enabled: true);
        sync.Observe(DisplayState.On);
        sync.Observe(DisplayState.Off);

        host.CompleteCurrent(commandSucceeded: false);

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
        private TaskCompletionSource<object?>? _currentOperation;

        public event EventHandler? OperationCompleted
        {
            add => _operationCompleted += value;
            remove => _operationCompleted -= value;
        }

        public bool Busy { get; private set; }

        public bool CompleteStartsSynchronously { get; init; }

        public int CompletionSubscribers => _operationCompleted?.GetInvocationList().Length ?? 0;

        public List<LightingMode> StartedModes { get; } = [];

        public bool TryStartLighting(LightingMode mode, out Task operation)
        {
            if (Busy)
            {
                operation = Task.CompletedTask;
                return false;
            }

            Busy = true;
            StartedModes.Add(mode);
            _currentOperation = new TaskCompletionSource<object?>(
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
            _currentOperation = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void CompleteCurrent(bool commandSucceeded = true)
        {
            ReleaseCurrentWithoutSignalingCompletion(commandSucceeded);
            SignalCompletion();
        }

        public void ReleaseCurrentWithoutSignalingCompletion(bool commandSucceeded = true)
        {
            Assert.IsTrue(Busy);
            Busy = false;
            _currentOperation!.SetResult(commandSucceeded ? new object() : null);
            _currentOperation = null;
        }

        public void SignalCompletion() => _operationCompleted?.Invoke(this, EventArgs.Empty);
    }
}
