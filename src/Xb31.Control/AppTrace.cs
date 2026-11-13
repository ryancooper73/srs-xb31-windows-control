using System.IO;

namespace Xb31.Control;

internal sealed class AppTrace
{
    private readonly object _gate = new();
    private readonly string? _path;

    internal AppTrace(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    private AppTrace()
    {
    }

    internal static AppTrace None { get; } = new();

    internal static AppTrace CreateForCurrentUser()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XB31 Control");
        return new AppTrace(Path.Combine(root, "activity.log"));
    }

    internal void Write(string eventName, string? detail = null)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string line = $"{DateTimeOffset.UtcNow:O} {eventName}";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                line += $" {detail}";
            }

            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics are never allowed to interfere with speaker control.
        }
    }
}
