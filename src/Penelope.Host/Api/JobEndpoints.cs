using System.Text.Json;
using Penelope.Host.Jobs;

namespace Penelope.Host.Api;

static class JobEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/jobs/{id}", (string id, JobRunner jobs) =>
        {
            var job = jobs.Find(id);
            return job is null ? Results.NotFound() : Results.Json(job, ApiJson.Options);
        });

        app.MapPost("/api/jobs/{id}/cancel", (string id, JobRunner jobs) =>
        {
            if (jobs.Find(id) is null)
            {
                return Results.NotFound();
            }

            var cancelled = jobs.Cancel(id);
            return Results.Json(new { cancelled }, ApiJson.Options, statusCode: cancelled ? StatusCodes.Status202Accepted : StatusCodes.Status409Conflict);
        });

        app.MapGet("/api/jobs/{id}/events", async (string id, HttpContext context, JobRunner jobs) =>
        {
            if (jobs.Find(id) is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await foreach (var snapshot in jobs.Watch(id, context.RequestAborted))
            {
                var json = JsonSerializer.Serialize(snapshot, ApiJson.Options);
                await context.Response.WriteAsync($"event: job\ndata: {json}\n\n");
                await context.Response.Body.FlushAsync();
            }
        });
    }
}
