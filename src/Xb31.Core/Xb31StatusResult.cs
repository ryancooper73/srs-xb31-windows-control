namespace Xb31.Core;

public sealed record Xb31StatusResult(
    Xb31QueryResult<LightingMode> Lighting,
    Xb31QueryResult<SoundMode> Sound,
    Xb31QueryResult<bool> AutoStandby,
    Xb31QueryResult<string> BatteryLabel)
{
    public bool IsComplete =>
        Lighting.HasValue && Sound.HasValue && AutoStandby.HasValue && BatteryLabel.HasValue;

    public bool HasAnyValue =>
        Lighting.HasValue || Sound.HasValue || AutoStandby.HasValue || BatteryLabel.HasValue;
}
