using System.Text.Json;

namespace Cycle_Data_Project;

internal sealed class RideDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MageneCycleData",
        "ride-data.json");

    public async Task<double> LoadTotalDistanceKmAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return 0;
        }

        await using var stream = File.OpenRead(_filePath);
        var savedData = await JsonSerializer.DeserializeAsync<SavedRideData>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Could not read saved ride data at '{_filePath}'.");

        if (!double.IsFinite(savedData.TotalDistanceKm) || savedData.TotalDistanceKm < 0)
        {
            throw new InvalidDataException($"Saved total distance in '{_filePath}' must be finite and non-negative.");
        }

        return savedData.TotalDistanceKm;
    }

    public async Task SaveTotalDistanceKmAsync(double totalDistanceKm, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(totalDistanceKm) || totalDistanceKm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalDistanceKm), "Total distance must be finite and non-negative.");
        }

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Could not determine ride-data storage directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_filePath}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new SavedRideData(totalDistanceKm),
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record SavedRideData(double TotalDistanceKm);
}
