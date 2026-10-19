namespace Cycle_Data_Project;

internal sealed class AppSettings
{
    public double WheelCircumferenceMm { get; init; } = 2105;
    public string SensorNameContains { get; init; } = "12772-1";

    public void Validate()
    {
        if (!double.IsFinite(WheelCircumferenceMm) || WheelCircumferenceMm <= 0 || WheelCircumferenceMm > 5000)
        {
            throw new InvalidDataException("WheelCircumferenceMm must be greater than 0 and no more than 5000 mm.");
        }

        if (string.IsNullOrWhiteSpace(SensorNameContains))
        {
            throw new InvalidDataException("SensorNameContains cannot be empty.");
        }
    }
}
