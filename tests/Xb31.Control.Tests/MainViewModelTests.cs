using Xb31.Control.ViewModels;
using Xb31.Core;

namespace Xb31.Control.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public void Construction_DoesNotContactOrCommandSpeakerAndStartsWithUnknownDashboardState()
    {
        var client = new FakeXb31Client();
        var viewModel = new MainViewModel(client);

        Assert.AreEqual(0, client.TotalCalls);
        Assert.IsNull(viewModel.SelectedLighting);
        Assert.AreEqual(DeviceState.NotChecked, viewModel.State);
        Assert.AreEqual("Not checked", viewModel.StatusText);
        Assert.AreEqual("--", viewModel.BatteryText);
        Assert.IsNull(viewModel.SelectedSoundMode);
        Assert.AreEqual("Unknown", viewModel.SoundStatusText);
        Assert.IsNull(viewModel.SelectedAutoStandby);
        Assert.AreEqual("Unknown", viewModel.AutoStandbyStatusText);
        Assert.AreEqual("No lighting command sent", viewModel.LastLightingText);
        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsTrue(viewModel.CanInteract);
        CollectionAssert.AreEqual(SoundModes.All.ToArray(), viewModel.SoundOptions.ToArray());
        CollectionAssert.AreEqual(AutoStandbyOptions.All.ToArray(), viewModel.AutoStandbyOptions.ToArray());
        CollectionAssert.AreEqual(LightingModes.All.ToArray(), viewModel.LightingOptions.ToArray());
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task InitializeAsync_ReadsOneSnapshotAndAppliesDashboardWithoutWriting()
    {
        var statusCompletion = new TaskCompletionSource<Xb31StatusResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeXb31Client
        {
            StatusTask = statusCompletion.Task
        };
        var viewModel = new MainViewModel(client);

        Task initialization = viewModel.InitializeAsync();

        Assert.AreEqual("Connecting", viewModel.StatusText);
        Assert.AreEqual(DeviceState.Connecting, viewModel.State);
        Assert.IsTrue(viewModel.IsBusy);
        Assert.AreEqual(1, client.StatusCalls);
        Assert.AreEqual(0, client.ProbeCalls + client.BatteryGetCalls + client.SoundGetCalls +
            client.AutoStandbyGetCalls + client.PowerOffCalls + client.LightingCalls +
            client.SoundSetCalls + client.AutoStandbySetCalls);

        statusCompletion.SetResult(CompleteStatus(
            LightingMode.Chill,
            SoundMode.Standard,
            true,
            "Fully charged"));
        await initialization;

        CollectionAssert.AreEqual(new[] { "GetStatus" }, client.CallOrder.ToArray());
        Assert.AreEqual("Fully charged", viewModel.BatteryText);
        Assert.AreEqual(LightingMode.Chill, viewModel.SelectedLighting!.Mode);
        Assert.AreEqual("Current: Chill", viewModel.LastLightingText);
        Assert.AreEqual(SoundMode.Standard, viewModel.SelectedSoundMode);
        Assert.AreEqual("Current: Standard", viewModel.SoundStatusText);
        Assert.IsTrue(viewModel.SelectedAutoStandby);
        Assert.AreEqual("Current: On", viewModel.AutoStandbyStatusText);
        Assert.AreEqual(DeviceState.Available, viewModel.State);
        Assert.AreEqual("Connected", viewModel.StatusText);
        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsTrue(viewModel.CanInteract);
    }

    [TestMethod]
    public async Task InitializeAsync_CleanupFailureReportsConnectedWithWarning()
    {
        var client = new FakeXb31Client
        {
            StatusResult = CompleteStatus(
                LightingMode.Chill,
                SoundMode.Standard,
                true,
                "Fully charged",
                Xb31Status.CleanupFailed)
        };
        var viewModel = new MainViewModel(client);

        await viewModel.InitializeAsync();

        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Connected; cleanup failed", viewModel.StatusText);
        Assert.AreEqual(1, client.StatusCalls);
        Assert.AreEqual(0, client.ProbeCalls);
        Assert.AreEqual(0, client.BatteryGetCalls + client.SoundGetCalls + client.AutoStandbyGetCalls);
    }

    [TestMethod]
    public async Task InitializeAsync_PartialSnapshotKeepsFreshValuesAndReportsIncompleteStatus()
    {
        var client = new FakeXb31Client
        {
            StatusResult = new Xb31StatusResult(
                Xb31QueryResult<LightingMode>.Success(LightingMode.Chill),
                FailedQuery<SoundMode>(Xb31Status.ReadFailed),
                Xb31QueryResult<bool>.Success(false),
                FailedQuery<string>(Xb31Status.ReadFailed))
        };
        var viewModel = new MainViewModel(client);

        await viewModel.InitializeAsync();

        Assert.AreEqual(LightingMode.Chill, viewModel.SelectedLighting!.Mode);
        Assert.AreEqual("Current: Chill", viewModel.LastLightingText);
        Assert.IsNull(viewModel.SelectedSoundMode);
        Assert.AreEqual("Unknown", viewModel.SoundStatusText);
        Assert.IsFalse(viewModel.SelectedAutoStandby);
        Assert.AreEqual("Current: Off", viewModel.AutoStandbyStatusText);
        Assert.AreEqual("--", viewModel.BatteryText);
        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Connected; some status unavailable", viewModel.StatusText);
    }

    [TestMethod]
    [DataRow(Xb31Status.Unavailable, DeviceState.Unavailable, "Speaker unavailable")]
    [DataRow(Xb31Status.ConnectionFailed, DeviceState.CommandFailed, "Connection failed")]
    [DataRow(Xb31Status.Timeout, DeviceState.CommandFailed, "Command timed out")]
    [DataRow(Xb31Status.ReadFailed, DeviceState.CommandFailed, "Status unavailable")]
    [DataRow(Xb31Status.MalformedResponse, DeviceState.CommandFailed, "Invalid response")]
    [DataRow(Xb31Status.UnexpectedFailure, DeviceState.CommandFailed, "Command failed")]
    public async Task InitializeAsync_StatusFailureMapsConciseStatusAndReenablesInteraction(
        Xb31Status status,
        DeviceState expectedState,
        string expectedStatusText)
    {
        var client = new FakeXb31Client
        {
            StatusResult = FailedStatus(status)
        };
        var viewModel = new MainViewModel(client);

        await viewModel.InitializeAsync();

        CollectionAssert.AreEqual(new[] { "GetStatus" }, client.CallOrder.ToArray());
        Assert.AreEqual(0, client.BatteryGetCalls + client.SoundGetCalls + client.AutoStandbyGetCalls);
        Assert.AreEqual(expectedState, viewModel.State);
        Assert.AreEqual(expectedStatusText, viewModel.StatusText);
        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsTrue(viewModel.CanInteract);
    }

    [TestMethod]
    public async Task SetSoundModeAsync_UsesSingleClientSetterAndPresentsConfirmedReadBack()
    {
        var client = new FakeXb31Client
        {
            SoundSetResult = ConfirmedSet(SoundMode.ExtraBass)
        };
        var viewModel = new MainViewModel(client);
        viewModel.SelectedSoundMode = SoundMode.ExtraBass;

        await viewModel.SetSoundModeAsync(SoundMode.ExtraBass);

        Assert.AreEqual(1, client.SoundSetCalls);
        Assert.AreEqual(SoundMode.ExtraBass, client.LastSoundMode);
        Assert.AreEqual(0, client.SoundGetCalls);
        Assert.AreEqual(SoundMode.ExtraBass, viewModel.SelectedSoundMode);
        Assert.AreEqual("Confirmed", viewModel.SoundStatusText);
        Assert.AreEqual(DeviceState.Available, viewModel.State);
        Assert.AreEqual("Sound mode confirmed", viewModel.StatusText);
    }

    [TestMethod]
    public async Task SetSoundModeAsync_MismatchedReadBackPresentsReturnedValueAsUnconfirmed()
    {
        var client = new FakeXb31Client
        {
            SoundSetResult = new Xb31SetResult<SoundMode>(
                SoundMode.LiveSound,
                Xb31Result.Success,
                Xb31QueryResult<SoundMode>.Success(SoundMode.ExtraBass))
        };
        var viewModel = new MainViewModel(client);

        viewModel.SelectedSoundMode = SoundMode.LiveSound;

        await viewModel.SetSoundModeAsync(SoundMode.LiveSound);

        Assert.AreEqual(1, client.SoundSetCalls);
        Assert.AreEqual(0, client.SoundGetCalls);
        Assert.AreEqual(SoundMode.ExtraBass, viewModel.SelectedSoundMode);
        Assert.AreEqual("Sent; unconfirmed", viewModel.SoundStatusText);
        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Sound mode sent; unconfirmed", viewModel.StatusText);

        client.SoundSetResult = new Xb31SetResult<SoundMode>(
            SoundMode.LiveSound,
            new Xb31Result(Xb31Status.WriteFailed),
            null);
        viewModel.SelectedSoundMode = SoundMode.LiveSound;

        await viewModel.SetSoundModeAsync(SoundMode.LiveSound);

        Assert.AreEqual(2, client.SoundSetCalls);
        Assert.AreEqual(0, client.SoundGetCalls);
        Assert.AreEqual(SoundMode.ExtraBass, viewModel.SelectedSoundMode);
    }

    [TestMethod]
    public async Task SetSoundModeAsync_WriteFailureClearsUnverifiedSelectionAndMapsFailure()
    {
        var client = new FakeXb31Client
        {
            SoundSetResult = new Xb31SetResult<SoundMode>(
                SoundMode.LiveSound,
                new Xb31Result(Xb31Status.WriteFailed),
                null)
        };
        var viewModel = new MainViewModel(client);

        viewModel.SelectedSoundMode = SoundMode.LiveSound;

        await viewModel.SetSoundModeAsync(SoundMode.LiveSound);

        Assert.AreEqual(1, client.SoundSetCalls);
        Assert.AreEqual(0, client.SoundGetCalls);
        Assert.IsNull(viewModel.SelectedSoundMode);
        Assert.AreEqual("Change failed", viewModel.SoundStatusText);
        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Command failed", viewModel.StatusText);
    }

    [TestMethod]
    public async Task SetSoundModeAsync_MissingReadBackRetainsRequestedValueAsLastSent()
    {
        var client = new FakeXb31Client
        {
            SoundSetResult = new Xb31SetResult<SoundMode>(
                SoundMode.LiveSound,
                Xb31Result.Success,
                null)
        };
        var viewModel = new MainViewModel(client);

        viewModel.SelectedSoundMode = SoundMode.LiveSound;

        await viewModel.SetSoundModeAsync(SoundMode.LiveSound);

        Assert.AreEqual(1, client.SoundSetCalls);
        Assert.AreEqual(0, client.SoundGetCalls);
        Assert.AreEqual(SoundMode.LiveSound, viewModel.SelectedSoundMode);
        Assert.AreEqual("Last sent", viewModel.SoundStatusText);
        Assert.AreEqual(DeviceState.Available, viewModel.State);
        Assert.AreEqual("Sound mode sent", viewModel.StatusText);
    }

    [TestMethod]
    public async Task SetAutoStandbyAsync_MissingReadBackRetainsRequestedValueAsLastSent()
    {
        var client = new FakeXb31Client
        {
            AutoStandbySetResult = new Xb31SetResult<bool>(true, Xb31Result.Success, null)
        };
        var viewModel = new MainViewModel(client);
        viewModel.SelectedAutoStandby = true;

        await viewModel.SetAutoStandbyAsync(true);

        Assert.AreEqual(1, client.AutoStandbySetCalls);
        Assert.IsTrue(client.LastAutoStandby);
        Assert.AreEqual(0, client.AutoStandbyGetCalls);
        Assert.IsTrue(viewModel.SelectedAutoStandby);
        Assert.AreEqual("Last sent", viewModel.AutoStandbyStatusText);
        Assert.AreEqual(DeviceState.Available, viewModel.State);
        Assert.AreEqual("Auto standby sent", viewModel.StatusText);
    }

    [TestMethod]
    public async Task SetAutoStandbyAsync_FailedReadBackReportsSentUnconfirmedWithoutExtraGetter()
    {
        var diagnostic = new InvalidOperationException("sensitive read detail");
        var client = new FakeXb31Client
        {
            AutoStandbySetResult = new Xb31SetResult<bool>(
                true,
                Xb31Result.Success,
                new Xb31QueryResult<bool>(Xb31Status.ReadFailed, false, default, diagnostic))
        };
        var viewModel = new MainViewModel(client);

        viewModel.SelectedAutoStandby = true;

        await viewModel.SetAutoStandbyAsync(true);

        Assert.AreEqual(1, client.AutoStandbySetCalls);
        Assert.AreEqual(0, client.AutoStandbyGetCalls);
        Assert.IsNull(viewModel.SelectedAutoStandby);
        Assert.AreEqual("Sent; unconfirmed", viewModel.AutoStandbyStatusText);
        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Auto standby sent; unconfirmed", viewModel.StatusText);
        Assert.IsFalse(viewModel.StatusText.Contains(diagnostic.Message, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetSoundModeAsync_CleanupFailurePresentsLastSentValueWithWarning()
    {
        var client = new FakeXb31Client
        {
            SoundSetResult = new Xb31SetResult<SoundMode>(
                SoundMode.Standard,
                new Xb31Result(Xb31Status.CleanupFailed),
                null)
        };
        var viewModel = new MainViewModel(client);
        viewModel.SelectedSoundMode = SoundMode.Standard;

        await viewModel.SetSoundModeAsync(SoundMode.Standard);

        Assert.AreEqual(1, client.SoundSetCalls);
        Assert.AreEqual(0, client.SoundGetCalls);
        Assert.AreEqual(SoundMode.Standard, viewModel.SelectedSoundMode);
        Assert.AreEqual("Last sent; cleanup failed", viewModel.SoundStatusText);
        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Sound mode sent; cleanup failed", viewModel.StatusText);
    }

    [TestMethod]
    public async Task BusyGate_IsSharedAcrossSoundAndAutoStandbyOperations()
    {
        var completion = new TaskCompletionSource<Xb31SetResult<SoundMode>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeXb31Client { SoundSetTask = completion.Task };
        var viewModel = new MainViewModel(client);
        viewModel.SelectedSoundMode = SoundMode.ExtraBass;
        viewModel.SelectedAutoStandby = true;

        Task first = viewModel.SetSoundModeAsync(SoundMode.ExtraBass);
        Task competing = viewModel.SetAutoStandbyAsync(true);

        Assert.IsFalse(first.IsCompleted);
        Assert.IsTrue(competing.IsCompleted);
        Assert.IsTrue(viewModel.IsBusy);
        Assert.IsFalse(viewModel.CanInteract);
        Assert.AreEqual(1, client.SoundSetCalls);
        Assert.AreEqual(0, client.AutoStandbySetCalls);

        completion.SetResult(ConfirmedSet(SoundMode.ExtraBass));
        await first;

        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsTrue(viewModel.CanInteract);
    }

    [TestMethod]
    public async Task SetLightingAsync_SendsOneDeliberateSelection()
    {
        var client = new FakeXb31Client();
        var viewModel = new MainViewModel(client);

        await viewModel.SetLightingAsync(LightingMode.Chill);

        Assert.AreEqual(1, client.LightingCalls);
        Assert.AreEqual(LightingMode.Chill, client.LastLightingMode);
        Assert.AreEqual("Last sent: Chill", viewModel.LastLightingText);
        Assert.AreEqual(DeviceState.Available, viewModel.State);
        Assert.AreEqual("Lighting sent", viewModel.StatusText);
    }

    [TestMethod]
    public async Task TryStartLighting_WhenIdle_StartsAndSignalsCompletion()
    {
        var completion = new TaskCompletionSource<Xb31Result>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeXb31Client { LightingTask = completion.Task };
        var viewModel = new MainViewModel(client);
        int completed = 0;
        viewModel.OperationCompleted += (_, _) => completed++;

        bool started = viewModel.TryStartLighting(LightingMode.LightOff, out Task operation);

        Assert.IsTrue(started);
        Assert.IsFalse(operation.IsCompleted);
        completion.SetResult(Xb31Result.Success);
        await operation;
        Assert.AreEqual(1, completed);
    }

    [TestMethod]
    public async Task TryStartLighting_WhenBusy_ReturnsFalseWithoutStarting()
    {
        var completion = new TaskCompletionSource<Xb31StatusResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeXb31Client { StatusTask = completion.Task };
        var viewModel = new MainViewModel(client);
        Task initialization = viewModel.InitializeAsync();

        Assert.IsFalse(viewModel.TryStartLighting(LightingMode.LightOff, out Task rejected));
        Assert.IsTrue(rejected.IsCompletedSuccessfully);
        Assert.AreEqual(0, client.LightingCalls);

        completion.SetResult(CompleteStatus(LightingMode.Chill, SoundMode.Standard, true, "Full"));
        await initialization;
    }

    [TestMethod]
    public async Task BusyGate_IgnoresDuplicateLightingOperationAndDoesNotBlockCaller()
    {
        var completion = new TaskCompletionSource<Xb31Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeXb31Client { LightingTask = completion.Task };
        var viewModel = new MainViewModel(client);

        Task first = viewModel.SetLightingAsync(LightingMode.Chill);
        Task duplicate = viewModel.SetLightingAsync(LightingMode.Rave);

        Assert.IsFalse(first.IsCompleted);
        Assert.IsTrue(duplicate.IsCompleted);
        Assert.IsTrue(viewModel.IsBusy);
        Assert.IsFalse(viewModel.CanInteract);
        Assert.AreEqual(DeviceState.Connecting, viewModel.State);
        Assert.AreEqual("Connecting", viewModel.StatusText);
        Assert.AreEqual(1, client.LightingCalls);

        completion.SetResult(new Xb31Result(Xb31Status.Timeout));
        await first;

        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsTrue(viewModel.CanInteract);
        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Command timed out", viewModel.StatusText);
    }

    [TestMethod]
    public async Task PowerOffAsync_SendsOnePowerOffCommand()
    {
        var client = new FakeXb31Client();
        var viewModel = new MainViewModel(client);

        await viewModel.PowerOffAsync();

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(0, client.ProbeCalls + client.LightingCalls + client.SoundSetCalls +
            client.AutoStandbySetCalls);
        Assert.AreEqual(DeviceState.Available, viewModel.State);
        Assert.AreEqual("Power off sent", viewModel.StatusText);
    }

    [TestMethod]
    [DataRow(Xb31Status.Unavailable, DeviceState.Unavailable, "Speaker unavailable")]
    [DataRow(Xb31Status.ConnectionFailed, DeviceState.CommandFailed, "Connection failed")]
    [DataRow(Xb31Status.Timeout, DeviceState.CommandFailed, "Command timed out")]
    [DataRow(Xb31Status.WriteFailed, DeviceState.CommandFailed, "Command failed")]
    [DataRow(Xb31Status.MalformedCommand, DeviceState.CommandFailed, "Invalid command")]
    [DataRow(Xb31Status.UnexpectedFailure, DeviceState.CommandFailed, "Command failed")]
    public async Task SetLightingAsync_MapsFailureToConciseUiStatus(
        Xb31Status status,
        DeviceState expectedState,
        string expectedStatusText)
    {
        var diagnostic = new InvalidOperationException("sensitive diagnostic detail");
        var client = new FakeXb31Client
        {
            LightingResult = new Xb31Result(status, diagnostic)
        };
        var viewModel = new MainViewModel(client);

        await viewModel.SetLightingAsync(LightingMode.Rave);

        Assert.AreEqual(expectedState, viewModel.State);
        Assert.AreEqual(expectedStatusText, viewModel.StatusText);
        Assert.IsFalse(viewModel.StatusText.Contains(diagnostic.Message, StringComparison.Ordinal));
        Assert.AreEqual("No lighting command sent", viewModel.LastLightingText);
    }

    [TestMethod]
    public async Task SetLightingAsync_CleanupFailureRecordsCompletedCommandWithWarning()
    {
        var client = new FakeXb31Client
        {
            LightingResult = new Xb31Result(Xb31Status.CleanupFailed)
        };
        var viewModel = new MainViewModel(client);

        await viewModel.SetLightingAsync(LightingMode.CalmMagenta);

        Assert.AreEqual("Last sent: Calm Magenta", viewModel.LastLightingText);
        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Command sent; cleanup failed", viewModel.StatusText);
    }

    [TestMethod]
    public async Task PowerOffAsync_CleanupFailureReportsCompletedCommandWithWarning()
    {
        var client = new FakeXb31Client
        {
            PowerOffResult = new Xb31Result(Xb31Status.CleanupFailed)
        };
        var viewModel = new MainViewModel(client);

        await viewModel.PowerOffAsync();

        Assert.AreEqual(1, client.PowerOffCalls);
        Assert.AreEqual(DeviceState.CommandFailed, viewModel.State);
        Assert.AreEqual("Power off sent; cleanup failed", viewModel.StatusText);
    }

    [TestMethod]
    public async Task ObservableProperties_RaisePropertyChangedForDashboardStateAndOperationTransitions()
    {
        var completion = new TaskCompletionSource<Xb31SetResult<SoundMode>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeXb31Client { SoundSetTask = completion.Task };
        var viewModel = new MainViewModel(client);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.SelectedLighting = LightingModes.All.Single(option => option.Mode == LightingMode.Chill);
        viewModel.SelectedSoundMode = SoundMode.Standard;
        viewModel.SelectedAutoStandby = false;
        Task operation = viewModel.SetSoundModeAsync(SoundMode.ExtraBass);
        completion.SetResult(ConfirmedSet(SoundMode.ExtraBass));
        await operation;

        CollectionAssert.IsSubsetOf(
            new[]
            {
                nameof(MainViewModel.SelectedLighting),
                nameof(MainViewModel.SelectedSoundMode),
                nameof(MainViewModel.SelectedAutoStandby),
                nameof(MainViewModel.SoundStatusText),
                nameof(MainViewModel.IsBusy),
                nameof(MainViewModel.CanInteract),
                nameof(MainViewModel.State),
                nameof(MainViewModel.StatusText)
            },
            changedProperties);
    }

    private static Xb31QueryResult<T> FailedQuery<T>(Xb31Status status) =>
        new(status, false, default!);

    private static Xb31StatusResult FailedStatus(Xb31Status status) =>
        new(
            FailedQuery<LightingMode>(status),
            FailedQuery<SoundMode>(status),
            FailedQuery<bool>(status),
            FailedQuery<string>(status));

    private static Xb31StatusResult CompleteStatus(
        LightingMode lighting,
        SoundMode sound,
        bool autoStandby,
        string battery,
        Xb31Status status = Xb31Status.Success) =>
        new(
            new Xb31QueryResult<LightingMode>(status, true, lighting),
            new Xb31QueryResult<SoundMode>(status, true, sound),
            new Xb31QueryResult<bool>(status, true, autoStandby),
            new Xb31QueryResult<string>(status, true, battery));

    private static Xb31SetResult<T> ConfirmedSet<T>(T value) =>
        new(value, Xb31Result.Success, Xb31QueryResult<T>.Success(value));

    private sealed class FakeXb31Client : IXb31Client
    {
        public int StatusCalls { get; private set; }
        public int ProbeCalls { get; private set; }
        public int PowerOffCalls { get; private set; }
        public int LightingCalls { get; private set; }
        public int BatteryGetCalls { get; private set; }
        public int SoundGetCalls { get; private set; }
        public int SoundSetCalls { get; private set; }
        public int AutoStandbyGetCalls { get; private set; }
        public int AutoStandbySetCalls { get; private set; }
        public int TotalCalls => StatusCalls + ProbeCalls + PowerOffCalls + LightingCalls + BatteryGetCalls +
            SoundGetCalls + SoundSetCalls + AutoStandbyGetCalls + AutoStandbySetCalls;
        public LightingMode? LastLightingMode { get; private set; }
        public SoundMode? LastSoundMode { get; private set; }
        public bool? LastAutoStandby { get; private set; }
        public List<string> CallOrder { get; } = [];
        public Xb31Result ProbeResult { get; init; } = Xb31Result.Success;
        public Xb31StatusResult StatusResult { get; init; } = CompleteStatus(
            LightingMode.Chill,
            SoundMode.Standard,
            true,
            "Fully charged");
        public Xb31Result PowerOffResult { get; init; } = Xb31Result.Success;
        public Xb31Result LightingResult { get; init; } = Xb31Result.Success;
        public Xb31QueryResult<string> BatteryResult { get; init; } =
            Xb31QueryResult<string>.Success("Fully charged");
        public Xb31QueryResult<SoundMode> SoundResult { get; init; } =
            Xb31QueryResult<SoundMode>.Success(SoundMode.Standard);
        public Xb31SetResult<SoundMode>? SoundSetResult { get; set; }
        public Xb31QueryResult<bool> AutoStandbyResult { get; init; } =
            Xb31QueryResult<bool>.Success(false);
        public Xb31SetResult<bool>? AutoStandbySetResult { get; init; }
        public Task<Xb31Result>? LightingTask { get; init; }
        public Task<Xb31Result>? ProbeTask { get; init; }
        public Task<Xb31StatusResult>? StatusTask { get; init; }
        public Task<Xb31QueryResult<string>>? BatteryTask { get; init; }
        public Task<Xb31QueryResult<SoundMode>>? SoundTask { get; init; }
        public Task<Xb31QueryResult<bool>>? AutoStandbyTask { get; init; }
        public Task<Xb31SetResult<SoundMode>>? SoundSetTask { get; init; }
        public TaskCompletionSource<object?> BatteryCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> SoundCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> AutoStandbyCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Xb31StatusResult> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            CallOrder.Add("GetStatus");
            return StatusTask ?? Task.FromResult(StatusResult);
        }

        public Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken = default)
        {
            ProbeCalls++;
            CallOrder.Add("Probe");
            return ProbeTask ?? Task.FromResult(ProbeResult);
        }

        public Task<Xb31Result> PowerOffAsync(CancellationToken cancellationToken = default)
        {
            PowerOffCalls++;
            CallOrder.Add("PowerOff");
            return Task.FromResult(PowerOffResult);
        }

        public Task<Xb31Result> SetLightingAsync(
            LightingMode mode,
            CancellationToken cancellationToken = default)
        {
            LightingCalls++;
            LastLightingMode = mode;
            CallOrder.Add("SetLighting");
            return LightingTask ?? Task.FromResult(LightingResult);
        }

        public Task<Xb31QueryResult<string>> GetBatteryLabelAsync(
            CancellationToken cancellationToken = default)
        {
            BatteryGetCalls++;
            CallOrder.Add("GetBattery");
            BatteryCalled.TrySetResult(null);
            return BatteryTask ?? Task.FromResult(BatteryResult);
        }

        public Task<Xb31QueryResult<SoundMode>> GetSoundModeAsync(
            CancellationToken cancellationToken = default)
        {
            SoundGetCalls++;
            CallOrder.Add("GetSound");
            SoundCalled.TrySetResult(null);
            return SoundTask ?? Task.FromResult(SoundResult);
        }

        public Task<Xb31SetResult<SoundMode>> SetSoundModeAsync(
            SoundMode mode,
            CancellationToken cancellationToken = default)
        {
            SoundSetCalls++;
            LastSoundMode = mode;
            CallOrder.Add("SetSound");
            return SoundSetTask ?? Task.FromResult(SoundSetResult ?? ConfirmedSet(mode));
        }

        public Task<Xb31QueryResult<bool>> GetAutoStandbyAsync(
            CancellationToken cancellationToken = default)
        {
            AutoStandbyGetCalls++;
            CallOrder.Add("GetAutoStandby");
            AutoStandbyCalled.TrySetResult(null);
            return AutoStandbyTask ?? Task.FromResult(AutoStandbyResult);
        }

        public Task<Xb31SetResult<bool>> SetAutoStandbyAsync(
            bool isOn,
            CancellationToken cancellationToken = default)
        {
            AutoStandbySetCalls++;
            LastAutoStandby = isOn;
            CallOrder.Add("SetAutoStandby");
            return Task.FromResult(AutoStandbySetResult ?? ConfirmedSet(isOn));
        }
    }
}
