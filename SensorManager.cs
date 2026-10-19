namespace Cycle_Data_Project;

internal sealed class SensorManager(AppSettings settings, CyclingData cyclingData) : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private string _state = "disconnected";
    private string _message = "Bike not connected";
    private Task? _connectionTask;

    public bool StartScan()
    {
        lock (_lock)
        {
            if (_state is "scanning" or "connecting" or "connected")
            {
                return false;
            }

            _state = "scanning";
            _message = $"Scanning for {settings.SensorNameContains}...";
            _connectionTask = Task.Run(() => RunConnectionAsync(_cancellation.Token));
            return true;
        }
    }

    public SensorStatus GetStatus()
    {
        lock (_lock)
        {
            return new SensorStatus(_state, _message);
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = await SensorConnection.ConnectAsync(
                settings.SensorNameContains,
                cyclingData,
                SetSensorStatus,
                cancellationToken);

            SetStatus("connected", $"Connected to {connection.DeviceName}");
            await connection.WaitForDisconnectAsync(cancellationToken);
            SetStatus("disconnected", "Bike disconnected");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("disconnected", "Bike not connected");
        }
        catch (Exception exception)
        {
            cyclingData.SetConnected(false);
            Console.Error.WriteLine($"Sensor connection failed: {exception.Message}");
            SetStatus("error", exception.Message);
        }
    }

    private void SetSensorStatus(string message)
    {
        var state = message.StartsWith("Scanning", StringComparison.OrdinalIgnoreCase)
            ? "scanning"
            : "connecting";
        SetStatus(state, message);
    }

    private void SetStatus(string state, string message)
    {
        lock (_lock)
        {
            _state = state;
            _message = message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_connectionTask is not null)
        {
            await _connectionTask;
        }

        _cancellation.Dispose();
    }
}

internal sealed record SensorStatus(string State, string Message);
