using Penelope.Abstractions;

namespace Penelope.Host.Api;

static class DiagnosticsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/diagnostics/logs", (IPenelopeDiagnosticLog log, int? limit) =>
        {
            var take = limit is > 0 and <= 500 ? limit.Value : 200;
            return Results.Json(log.Recent(take), ApiJson.Options);
        });

        app.MapGet("/api/diagnostics/events", (IEventBus events, int? limit) =>
        {
            var take = limit is > 0 and <= 500 ? limit.Value : 100;
            return Results.Json(events.Recent(take), ApiJson.Options);
        });
    }
}
