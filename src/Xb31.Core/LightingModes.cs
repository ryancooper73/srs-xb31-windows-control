namespace Xb31.Core;

public static class LightingModes
{
    private static readonly LightingOption[] Options =
    [
        new("Light Off", LightingMode.LightOff),
        new("Rave", LightingMode.Rave),
        new("Chill", LightingMode.Chill),
        new("Random Flash Off", LightingMode.RandomFlashOff),
        new("Hot", LightingMode.Hot),
        new("Cool", LightingMode.Cool),
        new("Strobe", LightingMode.Strobe),
        new("Calm Magenta", LightingMode.CalmMagenta),
        new("Calm Cyan", LightingMode.CalmCyan),
        new("Calm Lime", LightingMode.CalmLime),
        new("Calm Cinnabar", LightingMode.CalmCinnabar),
        new("Calm Daylight", LightingMode.CalmDaylight),
        new("Calm Light Bulb", LightingMode.CalmLightBulb)
    ];

    public static IReadOnlyList<LightingOption> All { get; } = Array.AsReadOnly(Options);

    public static string GetName(LightingMode mode) =>
        Options.Single(option => option.Mode == mode).Name;
}
