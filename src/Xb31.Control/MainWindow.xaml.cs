using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xb31.Control.ViewModels;
using Xb31.Core;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Xb31.Control;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;
    private bool _readyForSelection;
    private bool _settingControls;

    internal event Action<bool>? SyncLightingChanged;
    internal event Action<bool>? StartWithWindowsChanged;

    internal MainViewModel ViewModel => _viewModel;

    public MainWindow(IXb31Client client)
    {
        _viewModel = new MainViewModel(client);
        InitializeComponent();
        DataContext = _viewModel;
    }

    internal void SetAutomationSettings(bool syncEnabled, bool startWithWindows)
    {
        _settingControls = true;
        SyncLightingCheckBox.IsChecked = syncEnabled;
        StartWithWindowsCheckBox.IsChecked = startWithWindows;
        _settingControls = false;
    }

    internal void SetSyncLightingEnabled(bool enabled)
    {
        _settingControls = true;
        SyncLightingCheckBox.IsChecked = enabled;
        _settingControls = false;
    }

    internal void SetStartWithWindowsEnabled(bool enabled)
    {
        _settingControls = true;
        StartWithWindowsCheckBox.IsChecked = enabled;
        _settingControls = false;
    }

    internal void AllowApplicationExit() => _allowClose = true;

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _readyForSelection = true;
    }

    private async void LightingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_readyForSelection ||
            _viewModel.IsBusy ||
            ((ComboBox)sender).SelectedItem is not LightingOption option)
        {
            return;
        }

        await _viewModel.SetLightingAsync(option.Mode);
    }

    private async void SoundSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_readyForSelection ||
            _viewModel.IsBusy ||
            ((ComboBox)sender).SelectedValue is not SoundMode mode)
        {
            return;
        }

        await _viewModel.SetSoundModeAsync(mode);
    }

    private async void AutoStandbySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_readyForSelection ||
            _viewModel.IsBusy ||
            ((ComboBox)sender).SelectedValue is not bool isOn)
        {
            return;
        }

        await _viewModel.SetAutoStandbyAsync(isOn);
    }

    private void SyncLightingChecked(object sender, RoutedEventArgs e)
    {
        if (_settingControls)
        {
            return;
        }

        SyncLightingChanged?.Invoke(true);
    }

    private void SyncLightingUnchecked(object sender, RoutedEventArgs e)
    {
        if (_settingControls)
        {
            return;
        }

        SyncLightingChanged?.Invoke(false);
    }

    private void StartWithWindowsChecked(object sender, RoutedEventArgs e)
    {
        if (_settingControls)
        {
            return;
        }

        StartWithWindowsChanged?.Invoke(true);
    }

    private void StartWithWindowsUnchecked(object sender, RoutedEventArgs e)
    {
        if (_settingControls)
        {
            return;
        }

        StartWithWindowsChanged?.Invoke(false);
    }

    private async void PowerOffClicked(object sender, RoutedEventArgs e) =>
        await _viewModel.PowerOffAsync();

    private void TitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void MinimizeClicked(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
