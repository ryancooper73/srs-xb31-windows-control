using Microsoft.Win32;

namespace Xb31.Control;

internal sealed class UserSettings
{
    internal const string ApplicationKeyPath = @"Software\SRS-XB31";
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string RunValueName = "XB31 Control";

    private const string SyncLightingValueName = "SyncLightingWithDisplay";

    private readonly RegistryKey _currentUserRoot;
    private readonly string _executablePath;
    private readonly string _startupCommand;

    internal UserSettings(RegistryKey currentUserRoot, string executablePath)
    {
        _currentUserRoot = currentUserRoot;
        _executablePath = executablePath;
        _startupCommand = $"\"{executablePath}\" {StartupArguments.StartupSwitch}";
    }

    internal static UserSettings CreateForCurrentUser(string executablePath) =>
        new(Registry.CurrentUser, executablePath);

    /// <summary>
    /// The exact HKCU Run command line: the quoted executable plus the startup switch
    /// that keeps the window hidden when Windows launches the application.
    /// </summary>
    internal string StartupCommand => _startupCommand;

    internal bool SyncLightingWithDisplay
    {
        get
        {
            using RegistryKey? applicationKey = _currentUserRoot.OpenSubKey(ApplicationKeyPath);
            object? value = applicationKey?.GetValue(SyncLightingValueName);
            return value is not int storedValue || storedValue != 0;
        }
        set
        {
            using RegistryKey applicationKey = _currentUserRoot.CreateSubKey(ApplicationKeyPath);
            applicationKey.SetValue(
                SyncLightingValueName,
                value ? 1 : 0,
                RegistryValueKind.DWord);
        }
    }

    internal bool StartWithWindows
    {
        get
        {
            using RegistryKey? runKey = _currentUserRoot.OpenSubKey(RunKeyPath);
            return MatchesExecutable(runKey?.GetValue(RunValueName) as string);
        }
        set
        {
            if (value)
            {
                using RegistryKey runKey = _currentUserRoot.CreateSubKey(RunKeyPath);
                runKey.SetValue(RunValueName, _startupCommand, RegistryValueKind.String);
                return;
            }

            using RegistryKey? existingRunKey = _currentUserRoot.OpenSubKey(RunKeyPath, writable: true);
            existingRunKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Rewrites an enabled Run entry that points at this executable but predates
    /// the startup switch, so an upgrade keeps launching hidden.
    /// </summary>
    internal void RepairStartWithWindowsCommand()
    {
        using RegistryKey? runKey = _currentUserRoot.OpenSubKey(RunKeyPath, writable: true);
        if (runKey?.GetValue(RunValueName) is not string command ||
            !MatchesExecutable(command) ||
            string.Equals(command, _startupCommand, StringComparison.Ordinal))
        {
            return;
        }

        runKey.SetValue(RunValueName, _startupCommand, RegistryValueKind.String);
    }

    private bool MatchesExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        string trimmed = command.Trim();
        string executable;
        if (trimmed.StartsWith('"'))
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote < 0)
            {
                return false;
            }

            executable = trimmed[1..closingQuote];
        }
        else
        {
            int separator = trimmed.IndexOf(' ');
            executable = separator < 0 ? trimmed : trimmed[..separator];
        }

        return string.Equals(executable, _executablePath, StringComparison.OrdinalIgnoreCase);
    }
}
