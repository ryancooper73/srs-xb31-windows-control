using System.IO;

namespace Xb31.Control;

/// <summary>
/// A deliberately tiny append-only trace for the shutdown experiment. It records only
/// window-message classification and power-off outcomes, never device or user data.
/// </summary>
internal sealed class ShutdownTrace
{
    internal const string DirectoryName = "XB31 Control";
    internal const string FileName = "shutdown.log";
    private const long MaximumBytes = 64 * 1024;

    private readonly object _gate = new();
    private readonly string _path;
    private bool _enabled = true;

    internal ShutdownTrace(string path) =>
        _path = path ?? throw new ArgumentNullException(nameof(path));

    internal static ShutdownTrace CreateForCurrentUser() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DirectoryName,
            FileName));

    internal string FilePath => _path;

    internal void Write(string message)
    {
        lock (_gate)
        {
            if (!_enabled)
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

                var file = new FileInfo(_path);
                if (file.Exists && file.Length > MaximumBytes)
                {
                    file.Delete();
                }

                File.AppendAllText(
                    _path,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never interfere with shutdown; give up permanently.
                _enabled = false;
            }
        }
    }
}
