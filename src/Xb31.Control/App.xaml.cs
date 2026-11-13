using System.Windows;

namespace Xb31.Control;

public partial class App : System.Windows.Application
{
    private TrayApplicationController? _controller;
    private SingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool startHidden = StartupArguments.IsStartupLaunch(e.Args);
        string instanceName = StartupArguments.IsOfflineStartupTest(e.Args)
            ? SingleInstanceCoordinator.OfflineStartupTestInstanceName
            : SingleInstanceCoordinator.ProductionInstanceName;
        if (!SingleInstanceCoordinator.TryAcquire(instanceName, out var singleInstance) ||
            singleInstance is null)
        {
            Shutdown();
            return;
        }

        _singleInstance = singleInstance;
        try
        {
            var trace = AppTrace.CreateForCurrentUser();
            string executablePath = Environment.ProcessPath ?? "unavailable";
            string version = GetVersionForTrace(executablePath);
            trace.Write("Startup", $"path={executablePath}; version={version}; hidden={startHidden}");
            var client = StartupClientFactory.Create(e.Args);
            var window = new MainWindow(client);
            MainWindow = window;
            var settings = UserSettings.CreateForCurrentUser(
                Environment.ProcessPath ?? throw new InvalidOperationException(
                    "The XB31 Control executable path is unavailable."));
            var controller = new TrayApplicationController(
                this,
                window,
                window.ViewModel,
                settings,
                new DisplayStateMonitor(),
                startHidden,
                trace);
            _controller = controller;
            controller.Start();
            singleInstance.ListenForActivation(() =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(controller.ShowWindow);
                }
                catch (InvalidOperationException) when (
                    Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                }
            });
        }
        catch
        {
            _controller?.Dispose();
            singleInstance.Dispose();
            _singleInstance = null;
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _controller?.Dispose();
        base.OnExit(e);
    }

    internal static string GetVersionForTrace(string executablePath)
    {
        try
        {
            return System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath).ProductVersion ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
