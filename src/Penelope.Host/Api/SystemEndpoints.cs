using System.Text.Json;
using Penelope.Host.SystemInfo;

namespace Penelope.Host.Api;

static class SystemEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/system", () => Results.Json(MachineReadings.Read(), ApiJson.Options));

        app.MapGet("/api/system/stream", async (HttpContext context) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            while (!context.RequestAborted.IsCancellationRequested)
            {
                var json = JsonSerializer.Serialize(MachineReadings.Read(), ApiJson.Options);
                await context.Response.WriteAsync($"event: system\ndata: {json}\n\n");
                await context.Response.Body.FlushAsync();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }
}
