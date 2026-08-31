using Xb31.Core;

namespace Xb31.Control;

public static class StartupClientFactory
{
    public static IXb31Client Create(string[] args, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        return StartupArguments.IsOfflineStartupTest(args)
            ? new OfflineStartupClient()
            : new Xb31Client(report);
    }
}

internal sealed class OfflineStartupClient : IXb31Client
{
    private static readonly Xb31Result Unavailable = new(Xb31Status.Unavailable);

    public Task<Xb31StatusResult> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new Xb31StatusResult(
            UnavailableQuery<LightingMode>(),
            UnavailableQuery<SoundMode>(),
            UnavailableQuery<bool>(),
            UnavailableQuery<string>()));

    public Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable);

    public Task<Xb31Result> PowerOffAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable);

    public Task<Xb31Result> SetLightingAsync(
        LightingMode mode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable);

    public Task<Xb31QueryResult<string>> GetBatteryLabelAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(UnavailableQuery<string>());

    public Task<Xb31QueryResult<SoundMode>> GetSoundModeAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(UnavailableQuery<SoundMode>());

    public Task<Xb31SetResult<SoundMode>> SetSoundModeAsync(
        SoundMode mode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new Xb31SetResult<SoundMode>(mode, Unavailable, null));

    public Task<Xb31QueryResult<bool>> GetAutoStandbyAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(UnavailableQuery<bool>());

    public Task<Xb31SetResult<bool>> SetAutoStandbyAsync(
        bool isOn,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new Xb31SetResult<bool>(isOn, Unavailable, null));

    private static Xb31QueryResult<T> UnavailableQuery<T>() =>
        new(Xb31Status.Unavailable, false, default!);
}
