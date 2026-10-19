using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Cycle_Data_Project;

internal sealed class SensorConnection : IDisposable
{
    private static readonly Guid CyclingSpeedAndCadenceService = new("00001816-0000-1000-8000-00805f9b34fb");
    private static readonly Guid CscMeasurementCharacteristic = new("00002a5b-0000-1000-8000-00805f9b34fb");
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(30);

    private readonly BluetoothLEDevice _device;
    private readonly GattDeviceService _service;
    private readonly GattCharacteristic _measurement;
    private readonly CyclingData _cyclingData;
    private readonly TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string DeviceName { get; }

    private SensorConnection(
        BluetoothLEDevice device,
        GattDeviceService service,
        GattCharacteristic measurement,
        CyclingData cyclingData)
    {
        _device = device;
        _service = service;
        _measurement = measurement;
        _cyclingData = cyclingData;
        DeviceName = string.IsNullOrWhiteSpace(device.Name) ? "Cycling sensor" : device.Name;
        _measurement.ValueChanged += OnMeasurementChanged;
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
        _cyclingData.SetConnected(true);
    }

    public static async Task<SensorConnection> ConnectAsync(
        string sensorNameContains,
        CyclingData cyclingData,
        Action<string> setStatus,
        CancellationToken cancellationToken)
    {
        setStatus($"Scanning for {sensorNameContains}...");
        var address = await FindSensorAsync(sensorNameContains, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        setStatus("Sensor found. Connecting...");

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        if (device is null)
        {
            throw new InvalidOperationException("Windows could not open the discovered Bluetooth sensor.");
        }

        GattDeviceService? service = null;
        try
        {
            var services = await device.GetGattServicesForUuidAsync(
                CyclingSpeedAndCadenceService,
                BluetoothCacheMode.Uncached);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The sensor did not provide the Cycling Speed and Cadence service ({services.Status}).");
            }

            service = services.Services[0];
            var characteristics = await service.GetCharacteristicsForUuidAsync(
                CscMeasurementCharacteristic,
                BluetoothCacheMode.Uncached);
            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The sensor did not provide the CSC Measurement characteristic ({characteristics.Status}).");
            }

            var measurement = characteristics.Characteristics[0];
            var status = await measurement.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"Could not enable sensor notifications: {status}.");
            }

            return new SensorConnection(device, service, measurement, cyclingData);
        }
        catch
        {
            service?.Dispose();
            device.Dispose();
            throw;
        }
    }

    private static async Task<ulong> FindSensorAsync(string sensorNameContains, CancellationToken cancellationToken)
    {
        var found = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> onReceived = (_, args) =>
        {
            var name = args.Advertisement.LocalName;
            if (name.Contains(sensorNameContains, StringComparison.OrdinalIgnoreCase))
            {
                found.TrySetResult(args.BluetoothAddress);
            }
        };

        watcher.Received += onReceived;
        var started = false;
        try
        {
            watcher.Start();
            started = true;
            return await found.Task.WaitAsync(ScanTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"No Bluetooth sensor advertising a name containing '{sensorNameContains}' was found in 30 seconds.");
        }
        finally
        {
            watcher.Received -= onReceived;
            if (started)
            {
                watcher.Stop();
            }
        }
    }

    public Task WaitForDisconnectAsync(CancellationToken cancellationToken) =>
        _disconnected.Task.WaitAsync(cancellationToken);

    private void OnMeasurementChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        using var reader = DataReader.FromBuffer(args.CharacteristicValue);
        var packet = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(packet);
        _cyclingData.RecordNotification(packet);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        var connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
        _cyclingData.SetConnected(connected);
        if (!connected)
        {
            _disconnected.TrySetResult();
        }
    }

    public void Dispose()
    {
        _measurement.ValueChanged -= OnMeasurementChanged;
        _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _service.Dispose();
        _device.Dispose();
        _cyclingData.SetConnected(false);
        _cyclingData.ResetMeasurementBaseline();
    }
}
