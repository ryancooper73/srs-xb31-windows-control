namespace Xb31.Control;

internal static class StartupArguments
{
    internal const string StartupSwitch = "--startup";
    internal const string OfflineStartupTestSwitch = "--offline-startup-test";

    /// <summary>
    /// Windows launches the application through the HKCU Run entry with
    /// <see cref="StartupSwitch"/>, which keeps the main window hidden.
    /// </summary>
    internal static bool IsStartupLaunch(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return arguments.Contains(StartupSwitch, StringComparer.Ordinal);
    }

    internal static bool IsOfflineStartupTest(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return arguments.Contains(OfflineStartupTestSwitch, StringComparer.Ordinal) &&
            arguments.All(static argument =>
                argument is OfflineStartupTestSwitch or StartupSwitch);
    }
}
