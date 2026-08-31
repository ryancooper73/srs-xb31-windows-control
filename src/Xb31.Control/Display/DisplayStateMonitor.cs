using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Xb31.Control;

internal sealed class DisplayStateMonitor : IDisposable
{
    internal static readonly Guid SessionDisplayStatus =
        new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");

    private const int WmPowerBroadcast = 0x0218;
    private const int PbtPowerSettingChange = 0x8013;
    private const uint DeviceNotifyWindowHandle = 0;

    private readonly HwndSourceHook _hook;
    private readonly SystemShutdownPowerOff? _shutdownPowerOff;
    private HwndSource? _source;
    private IntPtr _notificationHandle;

    internal DisplayStateMonitor(SystemShutdownPowerOff? shutdownPowerOff = null)
    {
        _shutdownPowerOff = shutdownPowerOff;
        _hook = WindowProc;
    }

    internal event Action<DisplayState>? StateChanged;

    internal void Start(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_source is not null || _notificationHandle != IntPtr.Zero)
        {
            throw new InvalidOperationException("The display-state monitor has already started.");
        }

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(hwnd) ??
            throw new InvalidOperationException("The window handle is not available.");
        _source.AddHook(_hook);

        Guid guid = SessionDisplayStatus;
        _notificationHandle = RegisterPowerSettingNotification(
            hwnd,
            ref guid,
            DeviceNotifyWindowHandle);

        if (_notificationHandle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            _source.RemoveHook(_hook);
            _source = null;
            throw new Win32Exception(error);
        }
    }

    internal static bool TryMapState(uint raw, out DisplayState state)
    {
        state = (DisplayState)raw;
        return state is DisplayState.Off or DisplayState.On or DisplayState.Dim;
    }

    public void Dispose()
    {
        if (_notificationHandle != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
        }

        if (_source is not null)
        {
            _source.RemoveHook(_hook);
            _source = null;
        }
    }

    private IntPtr WindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        _shutdownPowerOff?.HandleMessage(hwnd, message, wParam, lParam);

        if (message != WmPowerBroadcast ||
            wParam.ToInt64() != PbtPowerSettingChange ||
            lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        PowerBroadcastSetting setting = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
        if (setting.PowerSetting != SessionDisplayStatus || setting.DataLength != sizeof(uint))
        {
            return IntPtr.Zero;
        }

        uint raw = unchecked((uint)Marshal.ReadInt32(
            IntPtr.Add(lParam, Marshal.SizeOf<PowerBroadcastSetting>())));
        if (TryMapState(raw, out DisplayState state))
        {
            StateChanged?.Invoke(state);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(
        IntPtr recipient,
        ref Guid powerSettingGuid,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PowerBroadcastSetting
    {
        internal Guid PowerSetting;
        internal uint DataLength;
    }
}
