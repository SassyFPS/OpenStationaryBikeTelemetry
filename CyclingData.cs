namespace Cycle_Data_Project;

internal sealed class CyclingData(double wheelCircumferenceMm, double totalDistanceKm)
{
    private static readonly TimeSpan SpeedTimeout = TimeSpan.FromSeconds(3);
    private const uint CounterRolloverWindow = 100_000;
    private const double MaximumPlausibleSpeedKph = 150;
    private readonly object _lock = new();
    private double _wheelCircumferenceMm = wheelCircumferenceMm;
    private uint? _lastWheelRevolutions;
    private ushort? _lastEventTime;
    private DateTimeOffset? _lastMeasurementAt;
    private DateTimeOffset? _lastNotificationAt;
    private long _notificationCount;
    private long _wheelNotificationCount;
    private byte? _lastMeasurementFlags;
    private string _lastMeasurementPacket = "No notifications received";
    private double _sessionDistanceMeters;
    private double _totalDistanceMeters = totalDistanceKm * 1000d;
    private double _speedKph;
    private bool _connected;

    public void SetConnected(bool connected)
    {
        lock (_lock)
        {
            _connected = connected;
        }
    }

    public double AdjustWheelCircumference(double deltaMm)
    {
        lock (_lock)
        {
            var circumferenceMm = _wheelCircumferenceMm + deltaMm;
            if (!double.IsFinite(circumferenceMm) || circumferenceMm <= 0 || circumferenceMm > 5000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaMm),
                    "Wheel circumference must stay above 0 and no more than 5000 mm.");
            }

            _wheelCircumferenceMm = circumferenceMm;
            return _wheelCircumferenceMm;
        }
    }

    public void ClearSessionDistance()
    {
        lock (_lock)
        {
            _sessionDistanceMeters = 0;
        }
    }

    public void SetTotalDistance(double distanceKm)
    {
        if (!double.IsFinite(distanceKm) || distanceKm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceKm), "Total distance must be a finite, non-negative value.");
        }

        lock (_lock)
        {
            _totalDistanceMeters = distanceKm * 1000d;
        }
    }

    public double GetTotalDistanceKm()
    {
        lock (_lock)
        {
            return _totalDistanceMeters / 1000d;
        }
    }

    public void RecordNotification(byte[] packet)
    {
        lock (_lock)
        {
            _notificationCount++;
            _lastNotificationAt = DateTimeOffset.UtcNow;
            _lastMeasurementPacket = Convert.ToHexString(packet);

            if (packet.Length == 0)
            {
                _lastMeasurementFlags = null;
                return;
            }

            var flags = packet[0];
            _lastMeasurementFlags = flags;
            if ((flags & 0x01) == 0 || packet.Length < 7)
            {
                return;
            }

            _wheelNotificationCount++;
            var wheelRevolutions = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(1, 4));
            var eventTime = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(5, 2));
            if (_lastWheelRevolutions is uint previousRevolutions && _lastEventTime is ushort previousEventTime)
            {
                var counterMovedBackwards = wheelRevolutions < previousRevolutions;
                var isCounterRollover = previousRevolutions >= uint.MaxValue - CounterRolloverWindow
                    && wheelRevolutions <= CounterRolloverWindow;

                if (counterMovedBackwards && !isCounterRollover)
                {
                    ResetBaseline(wheelRevolutions, eventTime);
                    return;
                }

                var revolutionDelta = unchecked(wheelRevolutions - previousRevolutions);
                var eventTimeDelta = unchecked((ushort)(eventTime - previousEventTime));
                if (revolutionDelta > 0 && eventTimeDelta == 0)
                {
                    ResetBaseline(wheelRevolutions, eventTime);
                    return;
                }

                var speedKph = eventTimeDelta == 0
                    ? 0
                    : revolutionDelta * _wheelCircumferenceMm * 3.6d * 1024d / 1000d / eventTimeDelta;

                if (!double.IsFinite(speedKph) || speedKph > MaximumPlausibleSpeedKph)
                {
                    ResetBaseline(wheelRevolutions, eventTime);
                    return;
                }

                var distanceMeters = revolutionDelta * _wheelCircumferenceMm / 1000d;
                _sessionDistanceMeters += distanceMeters;
                _totalDistanceMeters += distanceMeters;
                _speedKph = speedKph;
            }

            SetBaseline(wheelRevolutions, eventTime);
        }
    }

    public CyclingSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var speedIsCurrent = _lastMeasurementAt is DateTimeOffset lastMeasurementAt
                && DateTimeOffset.UtcNow - lastMeasurementAt <= SpeedTimeout;
            return new CyclingSnapshot(
                Math.Round(speedIsCurrent ? _speedKph : 0, 1),
                Math.Round(_sessionDistanceMeters / 1000d, 3),
                _totalDistanceMeters / 1000d,
                _wheelCircumferenceMm,
                _connected,
                _notificationCount,
                _wheelNotificationCount,
                _lastNotificationAt,
                _lastMeasurementFlags,
                _lastMeasurementPacket);
        }
    }

    public void ResetMeasurementBaseline()
    {
        lock (_lock)
        {
            _lastWheelRevolutions = null;
            _lastEventTime = null;
            _lastMeasurementAt = null;
            _lastNotificationAt = null;
            _notificationCount = 0;
            _wheelNotificationCount = 0;
            _lastMeasurementFlags = null;
            _lastMeasurementPacket = "No notifications received";
            _speedKph = 0;
        }
    }

    private void ResetBaseline(uint wheelRevolutions, ushort eventTime)
    {
        _speedKph = 0;
        SetBaseline(wheelRevolutions, eventTime);
    }

    private void SetBaseline(uint wheelRevolutions, ushort eventTime)
    {
        _lastWheelRevolutions = wheelRevolutions;
        _lastEventTime = eventTime;
        _lastMeasurementAt = DateTimeOffset.UtcNow;
    }
}

internal sealed record CyclingSnapshot(
    double SpeedKph,
    double SessionDistanceKm,
    double TotalDistanceKm,
    double WheelCircumferenceMm,
    bool Connected,
    long NotificationCount,
    long WheelNotificationCount,
    DateTimeOffset? LastNotificationAt,
    byte? LastMeasurementFlags,
    string LastMeasurementPacket);
