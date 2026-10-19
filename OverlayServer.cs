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

        if (request.Url?.AbsolutePath == "/api/connect" && request.HttpMethod == "POST")
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

        if (request.Url?.AbsolutePath == "/api/wheel-circumference" && request.HttpMethod == "POST")
        {
            using var document = await JsonDocument.ParseAsync(request.InputStream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("deltaMm", out var deltaElement)
                || !deltaElement.TryGetDouble(out var deltaMm)
                || (deltaMm != WheelCircumferenceStepMm && deltaMm != -WheelCircumferenceStepMm))
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Close();
                return;
            }

            try
            {
                cyclingData.AdjustWheelCircumference(deltaMm);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                response.StatusCode = (int)HttpStatusCode.Conflict;
                var error = JsonSerializer.SerializeToUtf8Bytes(new { error = exception.Message }, JsonOptions);
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = error.Length;
                await response.OutputStream.WriteAsync(error, cancellationToken);
                response.Close();
                return;
            }

            var updatedData = JsonSerializer.SerializeToUtf8Bytes(cyclingData.GetSnapshot(), JsonOptions);
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = updatedData.Length;
            await response.OutputStream.WriteAsync(updatedData, cancellationToken);
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
        switch (request.Url?.AbsolutePath)
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
}
