namespace Xb31.Core;

public static class SoundModes
{
    private static readonly SoundOption[] Options =
    [
        new("Standard", SoundMode.Standard),
        new("Extra Bass", SoundMode.ExtraBass),
        new("Live Sound", SoundMode.LiveSound)
    ];

    public static IReadOnlyList<SoundOption> All { get; } = Array.AsReadOnly(Options);

    public static string GetName(SoundMode mode) =>
        Options.Single(option => option.Mode == mode).Name;
}
