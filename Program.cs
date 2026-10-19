using System.ComponentModel;
using System.Diagnostics;
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

        var rideDataStore = new RideDataStore();
        var totalDistanceKm = await rideDataStore.LoadTotalDistanceKmAsync();
        var cyclingData = new CyclingData(settings.WheelCircumferenceMm, totalDistanceKm);
        await using var sensorManager = new SensorManager(settings, cyclingData);
        var overlayServer = new OverlayServer(cyclingData, sensorManager);
        var serverTask = overlayServer.RunAsync(cancellation.Token);
        var persistenceTask = PersistTotalDistanceAsync(cyclingData, rideDataStore, cancellation.Token);

        Console.WriteLine($"OBS overlay: {OverlayServer.Address}");
        TryOpenControlPage();
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }

        await serverTask;
        await persistenceTask;
    }

    private static async Task PersistTotalDistanceAsync(
        CyclingData cyclingData,
        RideDataStore rideDataStore,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await rideDataStore.SaveTotalDistanceKmAsync(cyclingData.GetTotalDistanceKm(), cancellationToken);
                }
                catch (IOException exception)
                {
                    Console.Error.WriteLine($"Could not save lifetime distance: {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    Console.Error.WriteLine($"Could not save lifetime distance: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        try
        {
            await rideDataStore.SaveTotalDistanceKmAsync(cyclingData.GetTotalDistanceKm());
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not save lifetime distance during shutdown: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Could not save lifetime distance during shutdown: {exception.Message}");
        }
    }

    private static void TryOpenControlPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OverlayServer.Address,
                UseShellExecute = true
            });
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine($"Could not open the control page automatically: {exception.Message}");
            Console.Error.WriteLine($"Open {OverlayServer.Address} in a browser.");
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"Could not open the control page automatically: {exception.Message}");
            Console.Error.WriteLine($"Open {OverlayServer.Address} in a browser.");
        }
    }
}
