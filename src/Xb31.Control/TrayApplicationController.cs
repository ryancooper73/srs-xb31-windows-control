using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Resources;
using Xb31.Control.ViewModels;
using Xb31.Core;
using Forms = System.Windows.Forms;

namespace Xb31.Control;

internal sealed class TrayApplicationController : IDisposable
{
    private readonly System.Windows.Application _application;
    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;
    private readonly IXb31OperationHost _operations;
    private readonly UserSettings _settings;
    private readonly DisplayStateMonitor _displayMonitor;
    private readonly SystemShutdownPowerOff? _shutdownPowerOff;
    private readonly bool _startHidden;

    private DisplayLightingSync? _displayLightingSync;
    private Stream? _trayIconStream;
    private Icon? _trayIconImage;
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _openMenuItem;
    private Forms.ToolStripMenuItem? _syncMenuItem;
    private Forms.ToolStripMenuItem? _lightOffMenuItem;
    private Forms.ToolStripMenuItem? _chillMenuItem;
    private Forms.ToolStripMenuItem? _powerOffMenuItem;
    private Forms.ToolStripMenuItem? _exitMenuItem;
    private bool _started;
    private bool _exitRequested;
    private bool _disposed;

    internal TrayApplicationController(
        System.Windows.Application application,
        MainWindow window,
        MainViewModel viewModel,
        UserSettings settings,
        DisplayStateMonitor displayMonitor,
        bool startHidden = false,
        SystemShutdownPowerOff? shutdownPowerOff = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _operations = viewModel;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _displayMonitor = displayMonitor ?? throw new ArgumentNullException(nameof(displayMonitor));
        _startHidden = startHidden;
        _shutdownPowerOff = shutdownPowerOff;
    }

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The tray application controller has already started.");
        }

        _started = true;
        _settings.RepairStartWithWindowsCommand();
        bool syncEnabled = _settings.SyncLightingWithDisplay;
        bool startWithWindows = _settings.StartWithWindows;
        _window.SetAutomationSettings(syncEnabled, startWithWindows);

        _displayLightingSync = new DisplayLightingSync(_operations, syncEnabled);
        _window.SyncLightingChanged += SyncLightingChanged;
        _window.StartWithWindowsChanged += StartWithWindowsChanged;
        _displayMonitor.StateChanged += DisplayStateChanged;
        if (_shutdownPowerOff is not null)
        {
            _application.SessionEnding += ApplicationSessionEnding;
        }

        CreateTrayIcon(syncEnabled);
        if (_startHidden)
        {
            // Windows started us: realize the handle so the message hooks and display
            // synchronization work, but leave the window hidden until the tray opens it.
            new WindowInteropHelper(_window).EnsureHandle();
        }
        else
        {
            _window.Show();
        }

        _displayMonitor.Start(_window);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.SyncLightingChanged -= SyncLightingChanged;
        _window.StartWithWindowsChanged -= StartWithWindowsChanged;
        _displayMonitor.StateChanged -= DisplayStateChanged;
        if (_shutdownPowerOff is not null)
        {
            _application.SessionEnding -= ApplicationSessionEnding;
        }
        _displayLightingSync?.Dispose();
        _displayMonitor.Dispose();

        if (_notifyIcon is not null)
        {
            _notifyIcon.DoubleClick -= NotifyIconDoubleClicked;
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip = null;
            _notifyIcon.Dispose();
        }

        if (_openMenuItem is not null)
        {
            _openMenuItem.Click -= OpenMenuItemClicked;
        }

        if (_syncMenuItem is not null)
        {
            _syncMenuItem.Click -= SyncMenuItemClicked;
        }

        if (_lightOffMenuItem is not null)
        {
            _lightOffMenuItem.Click -= LightOffMenuItemClicked;
        }

        if (_chillMenuItem is not null)
        {
            _chillMenuItem.Click -= ChillMenuItemClicked;
        }

        if (_powerOffMenuItem is not null)
        {
            _powerOffMenuItem.Click -= PowerOffMenuItemClicked;
        }

        if (_exitMenuItem is not null)
        {
            _exitMenuItem.Click -= ExitMenuItemClicked;
        }

        _trayMenu?.Dispose();
        _trayIconImage?.Dispose();
        _trayIconStream?.Dispose();
    }

    private void CreateTrayIcon(bool syncEnabled)
    {
        _openMenuItem = new Forms.ToolStripMenuItem("Open XB31 Control");
        _syncMenuItem = new Forms.ToolStripMenuItem("Sync lighting with display")
        {
            Checked = syncEnabled
        };
        _lightOffMenuItem = new Forms.ToolStripMenuItem("Light Off");
        _chillMenuItem = new Forms.ToolStripMenuItem("Chill");
        _powerOffMenuItem = new Forms.ToolStripMenuItem("Power Off");
        _exitMenuItem = new Forms.ToolStripMenuItem("Exit");

        _openMenuItem.Click += OpenMenuItemClicked;
        _syncMenuItem.Click += SyncMenuItemClicked;
        _lightOffMenuItem.Click += LightOffMenuItemClicked;
        _chillMenuItem.Click += ChillMenuItemClicked;
        _powerOffMenuItem.Click += PowerOffMenuItemClicked;
        _exitMenuItem.Click += ExitMenuItemClicked;

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.AddRange([
            _openMenuItem,
            _syncMenuItem,
            _lightOffMenuItem,
            _chillMenuItem,
            _powerOffMenuItem,
            _exitMenuItem
        ]);

        StreamResourceInfo resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/Xb31Control.ico"));
        _trayIconStream = resource.Stream;
        _trayIconImage = new Icon(resource.Stream);
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = _trayIconImage,
            Text = "XB31 Control",
            Visible = true
        };
        _notifyIcon.DoubleClick += NotifyIconDoubleClicked;
    }

    /// <summary>
    /// WPF surfaces its hidden-window session-ending lifecycle here. It is a deduplicated
    /// fallback when that notification arrives before the main window's query hook, and
    /// is never cancelled, so the session still ends.
    /// </summary>
    private void ApplicationSessionEnding(object sender, SessionEndingCancelEventArgs e) =>
        _shutdownPowerOff!.HandleSessionEnding(
            new WindowInteropHelper(_window).Handle,
            e.ReasonSessionEnding == ReasonSessionEnding.Logoff);

    private void SyncLightingChanged(bool enabled) => SetSyncLightingEnabled(enabled);

    private void StartWithWindowsChanged(bool enabled) =>
        _settings.StartWithWindows = enabled;

    private void SetSyncLightingEnabled(bool enabled)
    {
        _settings.SyncLightingWithDisplay = enabled;
        _displayLightingSync!.Enabled = enabled;
        _syncMenuItem!.Checked = enabled;
        _window.SetSyncLightingEnabled(enabled);
    }

    private void DisplayStateChanged(DisplayState state)
    {
        Debug.WriteLine($"Display state -> {state.ToString().ToUpperInvariant()}");
        _displayLightingSync!.Observe(state);
    }

    private void NotifyIconDoubleClicked(object? sender, EventArgs e) => ShowWindow();

    private void OpenMenuItemClicked(object? sender, EventArgs e) => ShowWindow();

    private void SyncMenuItemClicked(object? sender, EventArgs e) =>
        SetSyncLightingEnabled(!_syncMenuItem!.Checked);

    private async void LightOffMenuItemClicked(object? sender, EventArgs e) =>
        await _viewModel.SetLightingAsync(LightingMode.LightOff);

    private async void ChillMenuItemClicked(object? sender, EventArgs e) =>
        await _viewModel.SetLightingAsync(LightingMode.Chill);

    private async void PowerOffMenuItemClicked(object? sender, EventArgs e) =>
        await _viewModel.PowerOffAsync();

    private void ExitMenuItemClicked(object? sender, EventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        Dispose();
        _window.AllowApplicationExit();
        _window.Close();
        _application.Shutdown();
    }

    internal void ShowWindow()
    {
        if (_disposed ||
            _exitRequested ||
            _application.Dispatcher.HasShutdownStarted ||
            _application.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        SetForegroundWindow(new WindowInteropHelper(_window).Handle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
