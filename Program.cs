using System.Text.Json;

namespace Cycle_Data_Project;

internal static class Program
{
    private static async Task Main()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var settingsJson = await File.ReadAllTextAsync(settingsPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(settingsJson)
            ?? throw new InvalidDataException("Could not read appsettings.json.");
        settings.Validate();

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var cyclingData = new CyclingData(settings.WheelCircumferenceMm);
        await using var sensorManager = new SensorManager(settings, cyclingData);
        var overlayServer = new OverlayServer(cyclingData, sensorManager);
        var serverTask = overlayServer.RunAsync(cancellation.Token);

        Console.WriteLine($"OBS overlay: {OverlayServer.Address}");
        Console.WriteLine($"Open {OverlayServer.Address} in a browser to connect the bike.");
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }

        await serverTask;
    }
}
