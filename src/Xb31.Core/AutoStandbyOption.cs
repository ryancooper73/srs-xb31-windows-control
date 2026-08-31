namespace Xb31.Core;

public sealed record AutoStandbyOption(string Name, bool IsOn);

public static class AutoStandbyOptions
{
    private static readonly AutoStandbyOption[] Options =
    [
        new("Off", false),
        new("On", true)
    ];

    public static IReadOnlyList<AutoStandbyOption> All { get; } = Array.AsReadOnly(Options);
}
