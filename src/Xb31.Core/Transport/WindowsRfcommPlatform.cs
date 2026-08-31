using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;

namespace Xb31.Core.Transport;

internal sealed class WindowsRfcommPlatform : IRfcommPlatform
{
    private static readonly Guid ServiceUuid = new("B9B213CE-EEAB-49E4-8FD9-AA478ED1B26B");

    private readonly Action<string>? _report;

    internal WindowsRfcommPlatform(Action<string>? report = null) => _report = report;

    public async Task<IRfcommSession> ConnectAsync(CancellationToken cancellationToken)
    {
        BluetoothDevice? device = null;
        RfcommDeviceService? service = null;
        BluetoothDevice? serviceDevice = null;
        StreamSocket? socket = null;
        Xb31Status failureStatus = Xb31Status.Unavailable;

        try
        {
            using (var discoveryTimeout = CreateTimeout(cancellationToken, Xb31Timeouts.Discovery))
            {
                string pairedSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                DeviceInformationCollection pairedDevices = await DeviceInformation
                    .FindAllAsync(pairedSelector)
                    .AsTask(discoveryTimeout.Token)
                    .ConfigureAwait(false);
                string? targetId = Xb31DeviceSelector.SelectId(
                    pairedDevices.Select(info => new Xb31DeviceCandidate(
                        info.Id,
                        info.Name)));
                if (targetId is null)
                {
                    throw new Xb31TransportException(Xb31Status.Unavailable);
                }

                device = await BluetoothDevice.FromIdAsync(targetId)
                    .AsTask(discoveryTimeout.Token)
                    .ConfigureAwait(false);
                if (device is null || !device.DeviceInformation.Pairing.IsPaired)
                {
                    throw new Xb31TransportException(Xb31Status.Unavailable);
                }

                _report?.Invoke("XB31: target paired");

                RfcommServiceId serviceId = RfcommServiceId.FromUuid(ServiceUuid);
                string selector = RfcommDeviceService
                    .GetDeviceSelectorForBluetoothDeviceAndServiceId(device, serviceId);
                DeviceInformationCollection matches = await DeviceInformation.FindAllAsync(selector)
                    .AsTask(discoveryTimeout.Token)
                    .ConfigureAwait(false);
                if (matches.Count == 0)
                {
                    throw new Xb31TransportException(Xb31Status.Unavailable);
                }

                service = await RfcommDeviceService.FromIdAsync(matches[0].Id)
                    .AsTask(discoveryTimeout.Token)
                    .ConfigureAwait(false);
                serviceDevice = service?.Device;
                if (service is null || serviceDevice is null)
                {
                    throw new Xb31TransportException(Xb31Status.Unavailable);
                }

                _report?.Invoke("XB31: RFCOMM service found");
            }

            failureStatus = Xb31Status.ConnectionFailed;
            using (var connectionTimeout = CreateTimeout(cancellationToken, Xb31Timeouts.Connection))
            {
                socket = new StreamSocket();
                await socket.ConnectAsync(
                        service.ConnectionHostName,
                        service.ConnectionServiceName,
                        SocketProtectionLevel.BluetoothEncryptionWithAuthentication)
                    .AsTask(connectionTimeout.Token)
                    .ConfigureAwait(false);
            }

            _report?.Invoke("XB31: connected");
            return new WindowsRfcommSession(device, service, serviceDevice, socket, _report);
        }
        catch (OperationCanceledException exception)
        {
            DisposePartial(socket, serviceDevice, service, device);
            throw new Xb31TransportException(Xb31Status.Timeout, exception);
        }
        catch (Xb31TransportException)
        {
            DisposePartial(socket, serviceDevice, service, device);
            throw;
        }
        catch (Exception exception)
        {
            DisposePartial(socket, serviceDevice, service, device);
            throw new Xb31TransportException(failureStatus, exception);
        }
    }

    private static CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        CancellationTokenSource source = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private static void DisposePartial(
        StreamSocket? socket,
        BluetoothDevice? serviceDevice,
        RfcommDeviceService? service,
        BluetoothDevice? device)
    {
        try { socket?.Dispose(); } catch { }
        try { serviceDevice?.Dispose(); } catch { }
        try { service?.Dispose(); } catch { }
        try { device?.Dispose(); } catch { }
    }
}
