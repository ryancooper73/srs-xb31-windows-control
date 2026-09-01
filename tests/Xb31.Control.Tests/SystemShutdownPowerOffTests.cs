using System.Diagnostics;
using Xb31.Control;
using Xb31.Core;

namespace Xb31.Control.Tests;

[TestClass]
public sealed class SystemShutdownPowerOffTests
{
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;
    private const uint EndSessionCloseApp = 0x00000001;
    private const uint EndSessionCritical = 0x40000000;
    private const uint EndSessionLogoff = 0x80000000;

    private static readonly IntPtr Window = new(0x1234);

    [TestMethod]
    public void Classify_SystemShutdownHasNoSessionEndingFlags()
    {
        SessionEndClassification classification = SystemShutdownPowerOff.Classify(0);

        Assert.AreEqual(SessionEndKind.Shutdown, classification.Kind);
        Assert.IsFalse(classification.IsCritical);
        Assert.IsTrue(classification.IsEligibleForPowerOff);
    }

    [TestMethod]
    public void Classify_LogoffOutranksEveryOtherFlag()
    {
        foreach (uint flags in new[]
        {
            EndSessionLogoff,
            EndSessionLogoff | EndSessionCloseApp,
            EndSessionLogoff | EndSessionCritical,
            EndSessionLogoff | EndSessionCritical | EndSessionCloseApp
        })
        {
            SessionEndClassification classification = SystemShutdownPowerOff.Classify(flags);

            Assert.AreEqual(SessionEndKind.Logoff, classification.Kind, $"flags 0x{flags:X8}");
            Assert.IsFalse(classification.IsEligibleForPowerOff, $"flags 0x{flags:X8}");
        }
    }

    [TestMethod]
    public void Classify_CloseAppIsRestartManagerAndIsNotEligible()
    {
        SessionEndClassification classification =
            SystemShutdownPowerOff.Classify(EndSessionCloseApp);

        Assert.AreEqual(SessionEndKind.CloseApp, classification.Kind);
        Assert.IsFalse(classification.IsEligibleForPowerOff);
    }

    [TestMethod]
    public void Classify_CriticalShutdownStaysEligibleAndIsReportedAsCritical()
    {
        SessionEndClassification classification =
            SystemShutdownPowerOff.Classify(EndSessionCritical);

        Assert.AreEqual(SessionEndKind.Shutdown, classification.Kind);
        Assert.IsTrue(classification.IsCritical);
        Assert.IsTrue(classification.IsEligibleForPowerOff);
    }

    [TestMethod]
    public void UnrelatedWindowMessage_DoesNotAttemptPowerOff()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out _);

        handler.HandleMessage(Window, 0x0010, IntPtr.Zero, IntPtr.Zero);

        Assert.AreEqual(0, client.PowerOffCalls);
        Assert.AreEqual(0, blockReason.CreateCalls);
    }

    [TestMethod]
    [DataRow(EndSessionLogoff)]
    [DataRow(EndSessionCloseApp)]
    [DataRow(EndSessionLogoff | EndSessionCritical)]
    public void ExcludedSessionEndingReason_DoesNotAttemptPowerOff(uint flags)
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, Flags(flags));

        Assert.AreEqual(0, client.PowerOffCalls);
        Assert.AreEqual(0, blockReason.CreateCalls);
        CollectionAssert.Contains(trace, "skipped: not a system shutdown");
        CollectionAssert.Contains(trace, "returning TRUE");
    }

    [TestMethod]
    public void SystemShutdownQuery_PowersOffAndReleasesTheBlockReasonBeforeReturning()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.CreateCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
        Assert.AreEqual("Turning off SRS-XB31...", blockReason.LastReason);
        Assert.AreEqual(Window, blockReason.LastWindow);
        Assert.IsTrue(
            trace.Any(entry => entry.StartsWith("power off completed status=Success", StringComparison.Ordinal)),
            string.Join(" | ", trace));
        CollectionAssert.Contains(trace, "returning TRUE");
    }

    [TestMethod]
    public void RepeatedShutdownQuery_NeverOpensASecondTransaction()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);
        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.CreateCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
        CollectionAssert.Contains(trace, "skipped: power off already attempted this session");
    }

    [TestMethod]
    public void EndSessionAfterQuery_DoesNotStartASecondBluetoothTransaction()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.CreateCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
        Assert.IsTrue(
            trace.Any(entry => entry.StartsWith("WM_ENDSESSION wParam=1", StringComparison.Ordinal)),
            string.Join(" | ", trace));
        Assert.IsTrue(
            trace.Any(entry => entry.StartsWith("power off completed status=Success", StringComparison.Ordinal)),
            string.Join(" | ", trace));
    }

    [TestMethod]
    public void UnavailableSpeaker_HoldsTheHandlerUntilTheHardBoundThenReleases()
    {
        var client = new FakeClient(_ => Task.FromResult(new Xb31Result(Xb31Status.Unavailable)));
        var blockReason = new FakeBlockReason();
        var handler = new SystemShutdownPowerOff(
            client,
            TimeSpan.FromMilliseconds(120),
            blockReason,
            trace: null);
        var stopwatch = Stopwatch.StartNew();

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);
        stopwatch.Stop();

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
        Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(90), $"Elapsed: {stopwatch.Elapsed}");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Elapsed: {stopwatch.Elapsed}");
    }

    [TestMethod]
    public void PowerOffException_DoesNotEscapeAndStillReleasesTheBlockReason()
    {
        var client = new FakeClient(
            _ => Task.FromException<Xb31Result>(new InvalidOperationException("failure")));
        var blockReason = new FakeBlockReason();
        List<string> trace = [];
        var handler = new SystemShutdownPowerOff(
            client,
            TimeSpan.FromMilliseconds(120),
            blockReason,
            trace.Add);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
        Assert.IsTrue(
            trace.Any(entry => entry.Contains("InvalidOperationException", StringComparison.Ordinal)),
            string.Join(" | ", trace));
    }

    [TestMethod]
    public void SynchronousClientThrow_DoesNotEscapeAndStillReleasesTheBlockReason()
    {
        var client = new FakeClient(_ => throw new InvalidOperationException("immediate"));
        var blockReason = new FakeBlockReason();
        var handler = new SystemShutdownPowerOff(
            client,
            TimeSpan.FromMilliseconds(120),
            blockReason);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(1, blockReason.DestroyCalls);
    }

    [TestMethod]
    public void CleanupFailed_IsDeliveredAndReleasesWithoutWaitingForTheHardBound()
    {
        var client = new FakeClient(
            _ => Task.FromResult(new Xb31Result(Xb31Status.CleanupFailed)));
        var blockReason = new FakeBlockReason();
        var handler = new SystemShutdownPowerOff(
            client,
            TimeSpan.FromSeconds(2),
            blockReason);
        var stopwatch = Stopwatch.StartNew();

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);
        stopwatch.Stop();

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Elapsed: {stopwatch.Elapsed}");
    }

    [TestMethod]
    public void CanceledEndSession_DoesNotRepeatTheQueryTimePowerOff()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out _);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, IntPtr.Zero, IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.CreateCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
    }

    [TestMethod]
    public void CriticalShutdown_DoesNotBlockOrStartBluetooth()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, Flags(EndSessionCritical));
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), Flags(EndSessionCritical));

        Assert.AreEqual(0, client.PowerOffCalls);
        Assert.AreEqual(0, blockReason.CreateCalls);
        Assert.AreEqual(0, blockReason.DestroyCalls);
        CollectionAssert.Contains(trace, "skipped: critical shutdown");
    }

    [TestMethod]
    public void EndSessionWithoutAnArmedQuery_DoesNotStartBluetooth()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out _);

        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(0, client.PowerOffCalls);
        Assert.AreEqual(0, blockReason.CreateCalls);
    }

    [TestMethod]
    public void BlockReasonCreateFailure_DoesNotAttemptToDestroyItButStillPowersOff()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason { CreateResult = false };
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(0, blockReason.DestroyCalls);
        CollectionAssert.Contains(trace, "ShutdownBlockReasonCreate=False");
        CollectionAssert.Contains(trace, "returning TRUE");
    }

    [TestMethod]
    public void BlockReasonThrowing_DoesNotPreventTheHandlerFromReturning()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason { Throw = true };
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        CollectionAssert.Contains(trace, "returning TRUE");
    }

    [TestMethod]
    public void HungPowerOff_ReleasesTheCallerWithinTheHardBound()
    {
        var neverCompletes = new TaskCompletionSource<Xb31Result>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient(_ => neverCompletes.Task);
        var blockReason = new FakeBlockReason();
        var handler = new SystemShutdownPowerOff(
            client,
            TimeSpan.FromMilliseconds(120),
            blockReason,
            _ => { });
        var stopwatch = Stopwatch.StartNew();

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        stopwatch.Stop();
        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
        Assert.IsTrue(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Elapsed: {stopwatch.Elapsed}");
        neverCompletes.TrySetResult(Xb31Result.Success);
    }

    [TestMethod]
    public void HungPowerOff_CancelsTheTokenItHandedToTheClient()
    {
        var neverCompletes = new TaskCompletionSource<Xb31Result>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        var client = new FakeClient(token =>
        {
            observed = token;
            return neverCompletes.Task;
        });
        var handler = new SystemShutdownPowerOff(
            client,
            TimeSpan.FromMilliseconds(120),
            new FakeBlockReason(),
            _ => { });

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        // The bounded wait may return a timer tick before the deadline source fires.
        SpinWait.SpinUntil(() => observed.IsCancellationRequested, TimeSpan.FromSeconds(5));
        Assert.IsTrue(observed.IsCancellationRequested);
        neverCompletes.TrySetResult(Xb31Result.Success);
    }

    [TestMethod]
    public void HandlerDoesNotDeadlockOnASynchronizationContextItCannotResume()
    {
        Exception? failure = null;
        bool completed = false;
        var thread = new Thread(() =>
        {
            // A context whose posted continuations can only run once this thread stops
            // blocking - exactly the WPF message-pump situation during WM_QUERYENDSESSION.
            var context = new BlockedSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var client = new FakeClient(async _ =>
                {
                    await Task.Yield();
                    await Task.Delay(10).ConfigureAwait(true);
                    return Xb31Result.Success;
                });
                var handler = new SystemShutdownPowerOff(
                    client,
                    TimeSpan.FromSeconds(5),
                    new FakeBlockReason(),
                    _ => { });

                handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
                handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

                Assert.AreEqual(1, client.PowerOffCalls);
                Assert.AreEqual(0, context.PostCount);
                completed = true;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.IsBackground = true;
        thread.Start();

        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "The handler deadlocked.");
        Assert.IsNull(failure, failure?.ToString());
        Assert.IsTrue(completed);
    }

    [TestMethod]
    public void SessionEndingForAShutdown_PowersOffWhenTheWindowQueryDidNotRun()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleSessionEnding(Window, isLogoff: false);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.CreateCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
        CollectionAssert.Contains(trace, "Application.SessionEnding reason=Shutdown");
        CollectionAssert.Contains(trace, "session ending allowed to proceed");
    }

    [TestMethod]
    public void SessionEndingForALogoff_DoesNotAttemptPowerOff()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out List<string> trace);

        handler.HandleSessionEnding(Window, isLogoff: true);

        Assert.AreEqual(0, client.PowerOffCalls);
        Assert.AreEqual(0, blockReason.CreateCalls);
        CollectionAssert.Contains(trace, "Application.SessionEnding reason=Logoff");
        CollectionAssert.Contains(trace, "skipped: not a system shutdown");
    }

    [TestMethod]
    public void SessionEndingBeforeQuery_DoesNotPreventTheQueryTimeAttempt()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out _);

        handler.HandleSessionEnding(Window, isLogoff: false);
        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.CreateCalls);
    }

    [TestMethod]
    public void QueryThenSessionEnding_DoesNotRepeatTheCompletedAttempt()
    {
        var client = new FakeClient(_ => Task.FromResult(Xb31Result.Success));
        var blockReason = new FakeBlockReason();
        var handler = Create(client, blockReason, out _);

        handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        handler.HandleSessionEnding(Window, isLogoff: false);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.CreateCalls);

        handler.HandleMessage(Window, WmEndSession, new IntPtr(1), IntPtr.Zero);

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(1, blockReason.DestroyCalls);
    }

    private static SystemShutdownPowerOff Create(
        FakeClient client,
        FakeBlockReason blockReason,
        out List<string> trace)
    {
        List<string> entries = [];
        trace = entries;
        return new SystemShutdownPowerOff(
            client,
            TimeSpan.FromSeconds(5),
            blockReason,
            entry =>
            {
                lock (entries)
                {
                    entries.Add(entry);
                }
            });
    }

    private static IntPtr Flags(uint flags) => new(unchecked((int)flags));

    private sealed class BlockedSynchronizationContext : SynchronizationContext
    {
        internal int PostCount;

        public override void Post(SendOrPostCallback callback, object? state) =>
            Interlocked.Increment(ref PostCount);

        public override void Send(SendOrPostCallback callback, object? state) =>
            Interlocked.Increment(ref PostCount);
    }

    private sealed class FakeBlockReason : IShutdownBlockReason
    {
        internal int CreateCalls { get; private set; }

        internal int DestroyCalls { get; private set; }

        internal bool CreateResult { get; init; } = true;

        internal bool Throw { get; init; }

        internal string? LastReason { get; private set; }

        internal IntPtr LastWindow { get; private set; }

        public bool Create(IntPtr window, string reason)
        {
            CreateCalls++;
            LastReason = reason;
            LastWindow = window;
            return Throw ? throw new InvalidOperationException("create failed") : CreateResult;
        }

        public bool Destroy(IntPtr window)
        {
            DestroyCalls++;
            return Throw ? throw new InvalidOperationException("destroy failed") : true;
        }
    }

    private sealed class FakeClient(
        Func<CancellationToken, Task<Xb31Result>> powerOff) : IXb31Client
    {
        private int _powerOffCalls;

        public int PowerOffCalls => Volatile.Read(ref _powerOffCalls);

        public Task<Xb31Result> PowerOffAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _powerOffCalls);
            return powerOff(cancellationToken);
        }

        public Task<Xb31StatusResult> GetStatusAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Xb31Result> SetLightingAsync(
            LightingMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Xb31QueryResult<string>> GetBatteryLabelAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Xb31QueryResult<SoundMode>> GetSoundModeAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Xb31SetResult<SoundMode>> SetSoundModeAsync(
            SoundMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Xb31QueryResult<bool>> GetAutoStandbyAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Xb31SetResult<bool>> SetAutoStandbyAsync(
            bool isOn,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
