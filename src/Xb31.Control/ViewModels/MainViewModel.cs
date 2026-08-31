using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xb31.Core;

namespace Xb31.Control.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IXb31OperationHost
{
    private readonly IXb31Client _client;
    private int _operationActive;
    private LightingOption? _selectedLighting;
    private SoundMode? _selectedSoundMode;
    private bool? _selectedAutoStandby;
    private SoundMode? _lastKnownSoundMode;
    private bool? _lastKnownAutoStandby;
    private DeviceState _state = DeviceState.NotChecked;
    private string _statusText = "Not checked";
    private string _batteryText = "--";
    private string _soundStatusText = "Unknown";
    private string _autoStandbyStatusText = "Unknown";
    private string _lastLightingText = "No lighting command sent";
    private bool _isBusy;

    public MainViewModel(IXb31Client client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? OperationCompleted;

    public IReadOnlyList<LightingOption> LightingOptions => LightingModes.All;

    public IReadOnlyList<SoundOption> SoundOptions => SoundModes.All;

    public IReadOnlyList<AutoStandbyOption> AutoStandbyOptions => Xb31.Core.AutoStandbyOptions.All;

    public LightingOption? SelectedLighting
    {
        get => _selectedLighting;
        set => SetProperty(ref _selectedLighting, value);
    }

    public DeviceState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string BatteryText
    {
        get => _batteryText;
        private set => SetProperty(ref _batteryText, value);
    }

    public SoundMode? SelectedSoundMode
    {
        get => _selectedSoundMode;
        set => SetProperty(ref _selectedSoundMode, value);
    }

    public string SoundStatusText
    {
        get => _soundStatusText;
        private set => SetProperty(ref _soundStatusText, value);
    }

    public bool? SelectedAutoStandby
    {
        get => _selectedAutoStandby;
        set => SetProperty(ref _selectedAutoStandby, value);
    }

    public string AutoStandbyStatusText
    {
        get => _autoStandbyStatusText;
        private set => SetProperty(ref _autoStandbyStatusText, value);
    }

    public string LastLightingText
    {
        get => _lastLightingText;
        private set => SetProperty(ref _lastLightingText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    public bool CanInteract => !IsBusy;

    public async Task InitializeAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            Xb31StatusResult status = await _client.GetStatusAsync();
            ApplyStatus(status);
        }
        finally
        {
            EndOperation();
        }
    }

    private void ApplyStatus(Xb31StatusResult status)
    {
        WriteDiagnostic(status.Lighting);
        WriteDiagnostic(status.Sound);
        WriteDiagnostic(status.AutoStandby);
        WriteDiagnostic(status.BatteryLabel);

        if (status.Lighting.HasValue)
        {
            SelectedLighting = LightingOptions.Single(option => option.Mode == status.Lighting.Value);
            LastLightingText = $"Current: {LightingModes.GetName(status.Lighting.Value)}";
        }

        if (status.Sound.HasValue)
        {
            _lastKnownSoundMode = status.Sound.Value;
            SelectedSoundMode = status.Sound.Value;
            SoundStatusText = $"Current: {SoundModes.GetName(status.Sound.Value)}";
        }

        if (status.AutoStandby.HasValue)
        {
            _lastKnownAutoStandby = status.AutoStandby.Value;
            SelectedAutoStandby = status.AutoStandby.Value;
            AutoStandbyStatusText = status.AutoStandby.Value ? "Current: On" : "Current: Off";
        }

        if (status.BatteryLabel.HasValue)
            BatteryText = status.BatteryLabel.Value;

        if (status.IsComplete)
        {
            bool cleanupFailed = status.Lighting.Status == Xb31Status.CleanupFailed ||
                status.Sound.Status == Xb31Status.CleanupFailed ||
                status.AutoStandby.Status == Xb31Status.CleanupFailed ||
                status.BatteryLabel.Status == Xb31Status.CleanupFailed;
            State = cleanupFailed ? DeviceState.CommandFailed : DeviceState.Available;
            StatusText = cleanupFailed ? "Connected; cleanup failed" : "Connected";
            return;
        }

        if (status.HasAnyValue)
        {
            State = DeviceState.CommandFailed;
            StatusText = "Connected; some status unavailable";
            return;
        }

        ApplyFailure(status.Lighting.Status);
    }

    public async Task SetSoundModeAsync(SoundMode mode)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            Xb31SetResult<SoundMode> result = await _client.SetSoundModeAsync(mode);
            ApplySetResult(
                result,
                value =>
                {
                    _lastKnownSoundMode = value;
                    SelectedSoundMode = value;
                },
                () => SelectedSoundMode = _lastKnownSoundMode,
                value => SoundStatusText = value,
                "Sound mode");
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task SetAutoStandbyAsync(bool isOn)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            Xb31SetResult<bool> result = await _client.SetAutoStandbyAsync(isOn);
            ApplySetResult(
                result,
                value =>
                {
                    _lastKnownAutoStandby = value;
                    SelectedAutoStandby = value;
                },
                () => SelectedAutoStandby = _lastKnownAutoStandby,
                value => AutoStandbyStatusText = value,
                "Auto standby");
        }
        finally
        {
            EndOperation();
        }
    }

    public Task SetLightingAsync(LightingMode mode) =>
        TryStartLighting(mode, out Task operation) ? operation : Task.CompletedTask;

    public bool TryStartLighting(LightingMode mode, out Task operation)
    {
        if (!TryBeginOperation())
        {
            operation = Task.CompletedTask;
            return false;
        }

        operation = CompleteLightingAsync(mode);
        return true;
    }

    private async Task CompleteLightingAsync(LightingMode mode)
    {
        try
        {
            Xb31Result result = await _client.SetLightingAsync(mode);
            WriteDiagnostic(result);

            if (result.IsSuccess)
            {
                LastLightingText = $"Last sent: {LightingModes.GetName(mode)}";
                State = DeviceState.Available;
                StatusText = "Lighting sent";
            }
            else if (result.Status == Xb31Status.CleanupFailed)
            {
                LastLightingText = $"Last sent: {LightingModes.GetName(mode)}";
                State = DeviceState.CommandFailed;
                StatusText = "Command sent; cleanup failed";
            }
            else
            {
                ApplyFailure(result.Status);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task PowerOffAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            Xb31Result result = await _client.PowerOffAsync();
            WriteDiagnostic(result);

            if (result.IsSuccess)
            {
                State = DeviceState.Available;
                StatusText = "Power off sent";
            }
            else if (result.Status == Xb31Status.CleanupFailed)
            {
                State = DeviceState.CommandFailed;
                StatusText = "Power off sent; cleanup failed";
            }
            else
            {
                ApplyFailure(result.Status);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private bool TryBeginOperation()
    {
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
        {
            return false;
        }

        IsBusy = true;
        State = DeviceState.Connecting;
        StatusText = "Connecting";
        return true;
    }

    private void EndOperation()
    {
        Interlocked.Exchange(ref _operationActive, 0);
        IsBusy = false;
        OperationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySetResult<T>(
        Xb31SetResult<T> result,
        Action<T> applyReadBack,
        Action restoreLastKnown,
        Action<string> applySectionStatus,
        string settingName)
    {
        WriteDiagnostic(result.SendResult);
        if (result.ReadBack is not null)
        {
            WriteDiagnostic(result.ReadBack);
        }

        if (!result.WasSent)
        {
            restoreLastKnown();
            applySectionStatus("Change failed");
            ApplyFailure(result.SendResult.Status);
            return;
        }

        if (result.ReadBack is null)
        {
            applyReadBack(result.RequestedValue);
            if (result.SendResult.Status == Xb31Status.CleanupFailed)
            {
                applySectionStatus("Last sent; cleanup failed");
                State = DeviceState.CommandFailed;
                StatusText = $"{settingName} sent; cleanup failed";
            }
            else
            {
                applySectionStatus("Last sent");
                State = DeviceState.Available;
                StatusText = $"{settingName} sent";
            }

            return;
        }

        if (result.ReadBack is { HasValue: true } readBack)
        {
            applyReadBack(readBack.Value);
        }
        else
        {
            restoreLastKnown();
        }

        bool cleanupFailed = result.SendResult.Status == Xb31Status.CleanupFailed ||
            result.ReadBack?.Status == Xb31Status.CleanupFailed;

        if (result.IsConfirmed)
        {
            if (cleanupFailed)
            {
                applySectionStatus("Confirmed; cleanup failed");
                State = DeviceState.CommandFailed;
                StatusText = $"{settingName} confirmed; cleanup failed";
            }
            else
            {
                applySectionStatus("Confirmed");
                State = DeviceState.Available;
                StatusText = $"{settingName} confirmed";
            }

            return;
        }

        applySectionStatus(cleanupFailed
            ? "Sent; unconfirmed; cleanup failed"
            : "Sent; unconfirmed");
        State = DeviceState.CommandFailed;
        StatusText = cleanupFailed
            ? $"{settingName} sent; unconfirmed; cleanup failed"
            : $"{settingName} sent; unconfirmed";
    }

    private void ApplyFailure(Xb31Status status)
    {
        (State, StatusText) = status switch
        {
            Xb31Status.Unavailable => (DeviceState.Unavailable, "Speaker unavailable"),
            Xb31Status.ConnectionFailed => (DeviceState.CommandFailed, "Connection failed"),
            Xb31Status.Timeout => (DeviceState.CommandFailed, "Command timed out"),
            Xb31Status.MalformedCommand => (DeviceState.CommandFailed, "Invalid command"),
            Xb31Status.ReadFailed => (DeviceState.CommandFailed, "Status unavailable"),
            Xb31Status.MalformedResponse => (DeviceState.CommandFailed, "Invalid response"),
            _ => (DeviceState.CommandFailed, "Command failed")
        };
    }

    private static void WriteDiagnostic(Xb31Result result)
    {
        if (!result.IsSuccess && result.Diagnostic is not null)
        {
            Debug.WriteLine(result.Diagnostic.ToString());
        }
    }

    private static void WriteDiagnostic<T>(Xb31QueryResult<T> result)
    {
        if (!result.IsSuccess && result.Diagnostic is not null)
        {
            Debug.WriteLine(result.Diagnostic.ToString());
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
