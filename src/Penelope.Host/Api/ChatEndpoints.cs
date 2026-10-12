using System.Text.Json;
using Penelope.Abstractions;
using Penelope.Host.Agent;
using Penelope.Host.Plugins;

namespace Penelope.Host.Api;

static class ChatEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/chat/session", async (PluginCatalog catalog, AgentToolRegistry tools, CancellationToken cancellationToken) =>
        {
            var provider = Provider(catalog);
            if (provider is null)
            {
                return Results.Json(new
                {
                    ready = false,
                    provider = (string?)null,
                    model = (string?)null,
                    models = Array.Empty<string>(),
                    reason = "No model provider is installed.",
                    tools = tools.Tools.Select(tool => tool.Name).ToArray(),
                }, ApiJson.Options);
            }

            var availability = await provider.AvailabilityAsync(cancellationToken);
            return Results.Json(new
            {
                ready = availability.Ready,
                provider = availability.ProviderName,
                model = availability.Model,
                models = availability.Models,
                reason = availability.Reason,
                tools = tools.Tools.Select(tool => tool.Name).ToArray(),
            }, ApiJson.Options);
        });

        app.MapPost("/api/chat/stream", (HttpContext context, ChatBody body, PluginCatalog catalog, CancellationToken cancellationToken) =>
            Stream(context, agent: false, body, catalog, null, cancellationToken));

        app.MapPost("/api/agent/stream", (HttpContext context, ChatBody body, PluginCatalog catalog, AgentToolRegistry tools, CancellationToken cancellationToken) =>
            Stream(context, agent: true, body, catalog, tools, cancellationToken));
    }

    internal static IModelProvider? Provider(PluginCatalog catalog)
    {
        return catalog.Plugins.Select(plugin => plugin.Module).OfType<IModelProvider>().FirstOrDefault();
    }

    private static async Task Stream(
        HttpContext context,
        bool agent,
        ChatBody body,
        PluginCatalog catalog,
        AgentToolRegistry? tools,
        CancellationToken cancellationToken)
    {
        var problem = ChatTranscript.Read(body, out var history, out var requestedModel);
        if (problem is not null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = problem }, ApiJson.Options, cancellationToken);
            return;
        }

        var provider = Provider(catalog);
        if (provider is null)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { message = "No model provider is installed." }, ApiJson.Options, cancellationToken);
            return;
        }

        var availability = await provider.AvailabilityAsync(cancellationToken);
        if (!availability.Ready)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { message = availability.Reason ?? "A model isn't ready." }, ApiJson.Options, cancellationToken);
            return;
        }

        var model = requestedModel ?? availability.Model;
        if (model is null || !availability.Models.Contains(model, StringComparer.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "Choose a model from the list." }, ApiJson.Options, cancellationToken);
            return;
        }

        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        var events = agent
            ? AgentRunner.RunAsync(provider, model, history, tools?.Tools ?? [], cancellationToken)
            : ChatStreamer.CompleteAsync(provider, model, history, cancellationToken);
        await foreach (var ev in events)
        {
            var json = JsonSerializer.Serialize(new { text = ev.Text }, ApiJson.Options);
            await context.Response.WriteAsync($"event: {ev.Name}\ndata: {json}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
