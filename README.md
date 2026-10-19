# Magene Cycle Data

A small Windows app that scans for a Magene S3+ Bluetooth speed sensor on demand, reads wheel-rotation measurements, and serves a transparent overlay for OBS. Windows pairing is not required.

## Run the published app

Download and extract the Windows x64 app, then run `Cycle Data Project.exe`. It includes the .NET runtime, so installing the .NET SDK is not required. Open `http://127.0.0.1:8765/` when the app starts and click **Connect bike**. In OBS, use `http://127.0.0.1:8765/?overlay` as a Browser source.

The executable expects `appsettings.json` and the `wwwroot` folder alongside it. Keep all published files together.

## Build a Windows executable

Install the .NET 8 SDK, then run this from the project folder in PowerShell:

```powershell
dotnet publish ".\Cycle Data Project.csproj" --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --output ".\artifacts\publish\win-x64"
```

Run `artifacts\publish\win-x64\Cycle Data Project.exe`. This produces a self-contained Windows x64 executable; `appsettings.json` and `wwwroot` are published beside it.

## Put the project on GitHub

Create a repository on GitHub, then upload the project files. Do not upload `bin`, `obj`, or `artifacts`—they are build output and are excluded by `.gitignore`. The included GitHub Actions workflow builds the project on pushes and pull requests and makes the Windows publish available as a workflow artifact.

If the repository will be public and you want others to reuse or redistribute the code, choose and add a license. Without a license, standard copyright applies.

## Requirements

- Windows 10 version 2004 or newer
- .NET 8 SDK (only needed to build from source)
- The Magene S3+ switched on and broadcasting in speed mode

## Configure

Edit `appsettings.json` beside the executable (or in the project folder when running from source), then restart the app:

- `WheelCircumferenceMm`: your bike's wheel circumference in millimeters. Measure one full wheel rotation for the most accurate distance.
- `SensorNameContains`: a case-insensitive part of the name included in the sensor's Bluetooth advertisements.

The local control page also shows the active wheel circumference and has buttons to adjust it by 10 mm at a time. These changes apply immediately for the current app run; edit `appsettings.json` to make a new value the default next time.

## Run from source

From this folder:

```powershell
dotnet run
```

Open the local address printed by the app in a browser and click **Connect bike**. Keep the sensor awake and turn its wheel while scanning. The scan runs for up to 30 seconds. The local app continues serving zero stats if no sensor is connected.

In OBS, add a **Browser** source and set its URL to:

```text
http://127.0.0.1:8765/?overlay
```

The app must remain running while the overlay is in use. Distance starts at zero each time the app starts and is calculated from wheel rotations received during that run.
Speed returns to zero after three seconds without a wheel update.
Counter resets and samples above 150 km/h are discarded to prevent invalid sensor data from inflating speed or distance.

The service listens only on `127.0.0.1`, so it is reachable from this PC only. HTTPS is not needed for this localhost-only setup; browsers treat localhost as a secure context, and Bluetooth scanning is performed by the Windows app rather than the web page. Avoid changing the listener to a public network address without adding authentication and transport security.
