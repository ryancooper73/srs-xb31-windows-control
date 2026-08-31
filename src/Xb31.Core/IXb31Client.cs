namespace Xb31.Core;

public interface IXb31Client
{
    Task<Xb31StatusResult> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken = default);

    Task<Xb31Result> PowerOffAsync(CancellationToken cancellationToken = default);

    Task<Xb31Result> SetLightingAsync(
        LightingMode mode,
        CancellationToken cancellationToken = default);

    Task<Xb31QueryResult<string>> GetBatteryLabelAsync(
        CancellationToken cancellationToken = default);

    Task<Xb31QueryResult<SoundMode>> GetSoundModeAsync(
        CancellationToken cancellationToken = default);

    Task<Xb31SetResult<SoundMode>> SetSoundModeAsync(
        SoundMode mode,
        CancellationToken cancellationToken = default);

    Task<Xb31QueryResult<bool>> GetAutoStandbyAsync(
        CancellationToken cancellationToken = default);

    Task<Xb31SetResult<bool>> SetAutoStandbyAsync(
        bool isOn,
        CancellationToken cancellationToken = default);
}
