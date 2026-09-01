using System.Diagnostics;
using System.Runtime.InteropServices;
using Xb31.Core;

namespace Xb31.Control;

internal enum SessionEndKind
{
    Shutdown,
    Logoff,
    CloseApp
}

internal readonly record struct SessionEndClassification(SessionEndKind Kind, bool IsCritical)
{
    /// <summary>
    /// Only a genuine system shutdown or restart is worth a Bluetooth transaction. A logoff
    /// leaves the machine (and the speaker's host) running, and CLOSEAPP is the Restart
    /// Manager asking a single application to exit.
    /// </summary>
    internal bool IsEligibleForPowerOff => Kind == SessionEndKind.Shutdown;
}

internal interface IShutdownBlockReason
{
    bool Create(IntPtr window, string reason);

    bool Destroy(IntPtr window);
}

/// <summary>
/// Registers a shutdown-block reason and sends one bounded XB31 power-off while handling
/// WM_QUERYENDSESSION, before WPF begins its own application shutdown.
/// </summary>
internal sealed class SystemShutdownPowerOff
{
    internal const int WmQueryEndSession = 0x0011;
    internal const int WmEndSession = 0x0016;
    internal const string BlockReason = "Turning off SRS-XB31...";

    private const uint EndSessionCloseApp = 0x00000001;
    private const uint EndSessionCritical = 0x40000000;
    private const uint EndSessionLogoff = 0x80000000;

    /// <summary>
    /// Windows permits a shutdown-blocking application up to 30 seconds. Twenty seconds
    /// gives the RFCOMM transaction a useful recovery window without consuming the full
    /// system allowance. Successful delivery releases the block immediately.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly IXb31Client _client;
    private readonly TimeSpan _timeout;
    private readonly IShutdownBlockReason _blockReason;
    private readonly Action<string> _trace;
    private IntPtr _blockWindow;
    private bool _blockReasonCreated;
    private int _attemptStarted;
    private volatile bool _attemptInFlight;

    internal SystemShutdownPowerOff(IXb31Client client, Action<string>? trace = null)
        : this(client, DefaultTimeout, new Win32ShutdownBlockReason(), trace)
    {
    }

    internal SystemShutdownPowerOff(
        IXb31Client client,
        TimeSpan timeout,
        IShutdownBlockReason blockReason,
        Action<string>? trace = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
        _blockReason = blockReason ?? throw new ArgumentNullException(nameof(blockReason));
        _trace = trace ?? (static _ => { });
    }

    /// <summary>
    /// Records the shared client's transport progress, but only while a shutdown attempt is
    /// running, so ordinary tray activity never reaches the log. This is what distinguishes
    /// "connect never finished" from "frame written, only the settle was cut short".
    /// </summary>
    internal void TraceTransport(string message)
    {
        if (_attemptInFlight)
        {
            _trace($"transport: {message}");
        }
    }

    internal static SessionEndClassification Classify(uint flags)
    {
        SessionEndKind kind = (flags & EndSessionLogoff) != 0
            ? SessionEndKind.Logoff
            : (flags & EndSessionCloseApp) != 0
                ? SessionEndKind.CloseApp
                : SessionEndKind.Shutdown;
        return new SessionEndClassification(kind, (flags & EndSessionCritical) != 0);
    }

    /// <summary>
    /// Runs on the thread that owns the window handle, so the shutdown-block reason is
    /// created and destroyed from the correct thread.
    /// </summary>
    internal void HandleMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmQueryEndSession:
                HandleQueryEndSession(window, lParam);
                break;
            case WmEndSession:
                HandleEndSession(wParam, lParam);
                break;
        }
    }

    private void HandleQueryEndSession(IntPtr window, IntPtr lParam)
    {
        uint flags = unchecked((uint)lParam.ToInt64());
        SessionEndClassification classification = Classify(flags);
        _trace(
            $"WM_QUERYENDSESSION lParam=0x{flags:X8} kind={classification.Kind} " +
            $"critical={classification.IsCritical}");

        if (!classification.IsEligibleForPowerOff)
        {
            _trace("skipped: not a system shutdown");
            _trace("returning TRUE");
            return;
        }

        if (classification.IsCritical)
        {
            _trace("skipped: critical shutdown");
            _trace("returning TRUE");
            return;
        }

        AttemptOnce(window);
        _trace("returning TRUE");
    }

    /// <summary>
    /// WPF raises this from its own hidden window's WM_QUERYENDSESSION. Whichever entry
    /// point arrives first owns the single bounded transaction. The event is never
    /// cancelled.
    /// </summary>
    internal void HandleSessionEnding(IntPtr window, bool isLogoff)
    {
        _trace($"Application.SessionEnding reason={(isLogoff ? "Logoff" : "Shutdown")}");
        if (isLogoff)
        {
            _trace("skipped: not a system shutdown");
        }
        else
        {
            AttemptOnce(window);
        }

        _trace("session ending allowed to proceed");
    }

    private void AttemptOnce(IntPtr window)
    {
        if (Interlocked.Exchange(ref _attemptStarted, 1) != 0)
        {
            _trace("skipped: power off already attempted this session");
            return;
        }

        _blockWindow = window;
        try
        {
            _blockReasonCreated = _blockReason.Create(window, BlockReason);
            _trace($"ShutdownBlockReasonCreate={_blockReasonCreated}");
        }
        catch (Exception exception)
        {
            _trace($"ShutdownBlockReasonCreate threw {exception.GetType().Name}");
        }

        AttemptPowerOff();
    }

    private void HandleEndSession(IntPtr wParam, IntPtr lParam)
    {
        uint flags = unchecked((uint)lParam.ToInt64());
        SessionEndClassification classification = Classify(flags);
        bool ending = wParam != IntPtr.Zero;
        _trace(
            $"WM_ENDSESSION wParam={(ending ? 1 : 0)} lParam=0x{flags:X8} " +
            $"kind={classification.Kind} critical={classification.IsCritical}");

        if (!ending)
        {
            _trace("shutdown canceled");
            return;
        }

        _trace("session end confirmed; query-time power off already completed");
    }

    private void AttemptPowerOff()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            bool delivered = RunBoundedPowerOff(stopwatch);
            if (!delivered)
            {
                HoldUntilDeadline(stopwatch);
            }
        }
        catch (Exception exception)
        {
            _trace(
                $"power off aborted after {stopwatch.ElapsedMilliseconds} ms: " +
                exception.GetType().Name);
        }
        finally
        {
            ReleaseBlockReason();
        }
    }

    private bool RunBoundedPowerOff(Stopwatch stopwatch)
    {
        _trace("power off attempt started");
        _attemptInFlight = true;
        var cancellation = new CancellationTokenSource(_timeout);
        Task<Xb31Result> attempt;
        try
        {
            // Task.Run keeps the operation off the UI synchronization context, so nothing
            // it awaits needs the blocked message-pump thread to resume.
            attempt = Task.Run(
                () => _client.PowerOffAsync(cancellation.Token),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _attemptInFlight = false;
            cancellation.Dispose();
            _trace($"power off could not start: {exception.GetType().Name}");
            return false;
        }

        try
        {
            if (!attempt.Wait(_timeout))
            {
                _attemptInFlight = false;
                _trace($"power off timed out after {stopwatch.ElapsedMilliseconds} ms");
                DisposeWhenFinished(attempt, cancellation);
                return false;
            }
        }
        catch (Exception exception)
        {
            _attemptInFlight = false;
            _trace(
                $"power off failed after {stopwatch.ElapsedMilliseconds} ms: " +
                Describe(exception));
            DisposeWhenFinished(attempt, cancellation);
            return false;
        }

        _attemptInFlight = false;
        Xb31Result result = attempt.GetAwaiter().GetResult();
        bool delivered = result.Status is Xb31Status.Success or Xb31Status.CleanupFailed;
        _trace(
            $"power off {(delivered ? "completed" : "failed")} status={result.Status} " +
            $"elapsed={stopwatch.ElapsedMilliseconds} ms");
        cancellation.Dispose();
        return delivered;
    }

    private void HoldUntilDeadline(Stopwatch stopwatch)
    {
        TimeSpan remaining = _timeout - stopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        _trace($"power off unsuccessful; holding shutdown for {remaining.TotalMilliseconds:F0} ms");
        Thread.Sleep(remaining);
        _trace($"shutdown hold released after {stopwatch.ElapsedMilliseconds} ms");
    }

    private void ReleaseBlockReason()
    {
        if (!_blockReasonCreated)
        {
            _blockWindow = IntPtr.Zero;
            return;
        }

        bool destroyed = false;
        try
        {
            destroyed = _blockReason.Destroy(_blockWindow);
        }
        catch (Exception exception)
        {
            _trace($"ShutdownBlockReasonDestroy threw {exception.GetType().Name}");
        }
        finally
        {
            _blockReasonCreated = false;
            _blockWindow = IntPtr.Zero;
        }

        _trace($"ShutdownBlockReasonDestroy={destroyed}");
    }

    private static string Describe(Exception exception) =>
        exception is AggregateException aggregate && aggregate.InnerException is not null
            ? aggregate.InnerException.GetType().Name
            : exception.GetType().Name;

    /// <summary>
    /// The abandoned attempt still holds the token, so the source outlives this handler
    /// and is released once the operation unwinds.
    /// </summary>
    private static void DisposeWhenFinished(Task task, CancellationTokenSource cancellation) =>
        _ = task.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}

internal sealed class Win32ShutdownBlockReason : IShutdownBlockReason
{
    public bool Create(IntPtr window, string reason) =>
        window != IntPtr.Zero && ShutdownBlockReasonCreate(window, reason);

    public bool Destroy(IntPtr window) =>
        window != IntPtr.Zero && ShutdownBlockReasonDestroy(window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShutdownBlockReasonCreate(IntPtr window, string reason);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShutdownBlockReasonDestroy(IntPtr window);
}
