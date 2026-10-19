using System.Net;
using System.Text.Json;

namespace Cycle_Data_Project;

internal sealed class OverlayServer(CyclingData cyclingData, SensorManager sensorManager)
{
    public const string Address = "http://127.0.0.1:8765/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    private const double WheelCircumferenceStepMm = 10;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(Address);
        listener.Start();
        using var stopListener = cancellationToken.Register(listener.Stop);

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await HandleRequestAsync(context, cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url?.AbsolutePath;

        if (request.HttpMethod == "POST")
        {
            if (path == "/api/connect")
            {
                var started = sensorManager.StartScan();
                var result = JsonSerializer.SerializeToUtf8Bytes(
                    new { started, status = sensorManager.GetStatus() },
                    JsonOptions);
                response.StatusCode = started ? (int)HttpStatusCode.Accepted : (int)HttpStatusCode.Conflict;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = result.Length;
                await response.OutputStream.WriteAsync(result, cancellationToken);
                response.Close();
                return;
            }

            if (path == "/api/session/clear")
            {
                cyclingData.ClearSessionDistance();
                await WriteJsonResponseAsync(response, cyclingData.GetSnapshot(), cancellationToken);
                return;
            }

            if (path == "/api/total-distance")
            {
                try
                {
                    using var document = await JsonDocument.ParseAsync(request.InputStream, cancellationToken: cancellationToken);
                    if (!document.RootElement.TryGetProperty("distanceKm", out var distanceElement)
                        || !distanceElement.TryGetDouble(out var distanceKm)
                        || !double.IsFinite(distanceKm)
                        || distanceKm < 0)
                    {
                        await WriteErrorResponseAsync(
                            response,
                            HttpStatusCode.BadRequest,
                            "Total distance must be a finite, non-negative number.",
                            cancellationToken);
                        return;
                    }

                    cyclingData.SetTotalDistance(distanceKm);
                    await WriteJsonResponseAsync(response, cyclingData.GetSnapshot(), cancellationToken);
                }
                catch (JsonException)
                {
                    await WriteErrorResponseAsync(
                        response,
                        HttpStatusCode.BadRequest,
                        "Request body must be valid JSON.",
                        cancellationToken);
                }

                return;
            }

            if (path == "/api/wheel-circumference")
            {
                try
                {
                    using var document = await JsonDocument.ParseAsync(request.InputStream, cancellationToken: cancellationToken);
                    if (!document.RootElement.TryGetProperty("deltaMm", out var deltaElement)
                        || !deltaElement.TryGetDouble(out var deltaMm)
                        || (deltaMm != WheelCircumferenceStepMm && deltaMm != -WheelCircumferenceStepMm))
                    {
                        await WriteErrorResponseAsync(
                            response,
                            HttpStatusCode.BadRequest,
                            "Wheel circumference can only be adjusted by 10 mm.",
                            cancellationToken);
                        return;
                    }

                    cyclingData.AdjustWheelCircumference(deltaMm);
                    await WriteJsonResponseAsync(response, cyclingData.GetSnapshot(), cancellationToken);
                }
                catch (JsonException)
                {
                    await WriteErrorResponseAsync(
                        response,
                        HttpStatusCode.BadRequest,
                        "Request body must be valid JSON.",
                        cancellationToken);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    await WriteErrorResponseAsync(
                        response,
                        HttpStatusCode.Conflict,
                        exception.Message,
                        cancellationToken);
                }

                return;
            }

            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        if (request.HttpMethod != "GET" && request.HttpMethod != "HEAD")
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.Close();
            return;
        }

        byte[] content;
        switch (path)
        {
            case "/":
                content = await File.ReadAllBytesAsync(Path.Combine(_webRoot, "index.html"), cancellationToken);
                response.ContentType = "text/html; charset=utf-8";
                break;
            case "/styles.css":
                content = await File.ReadAllBytesAsync(Path.Combine(_webRoot, "styles.css"), cancellationToken);
                response.ContentType = "text/css; charset=utf-8";
                break;
            case "/api/data":
                content = JsonSerializer.SerializeToUtf8Bytes(cyclingData.GetSnapshot(), JsonOptions);
                response.ContentType = "application/json; charset=utf-8";
                response.Headers[HttpResponseHeader.CacheControl] = "no-store";
                break;
            case "/api/status":
                content = JsonSerializer.SerializeToUtf8Bytes(sensorManager.GetStatus(), JsonOptions);
                response.ContentType = "application/json; charset=utf-8";
                response.Headers[HttpResponseHeader.CacheControl] = "no-store";
                break;
            default:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
                return;
        }

        response.ContentLength64 = content.Length;
        if (request.HttpMethod == "GET")
        {
            await response.OutputStream.WriteAsync(content, cancellationToken);
        }

        response.Close();
    }

    private static async Task WriteJsonResponseAsync(
        HttpListenerResponse response,
        object value,
        CancellationToken cancellationToken)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        response.ContentType = "application/json; charset=utf-8";
        response.Headers[HttpResponseHeader.CacheControl] = "no-store";
        response.ContentLength64 = content.Length;
        await response.OutputStream.WriteAsync(content, cancellationToken);
        response.Close();
    }

    private static async Task WriteErrorResponseAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        response.StatusCode = (int)statusCode;
        await WriteJsonResponseAsync(response, new { error = message }, cancellationToken);
    }
}
