using Microsoft.AspNetCore.Http;
using Penelope.Abstractions;

namespace Penelope.Modules.Ollama;

public sealed class OllamaApi
{
    private readonly OllamaClient _client;
    private readonly OllamaModule _module;
    private IJobQueue? _jobs;

    public OllamaApi(OllamaClient client, OllamaModule module)
    {
        _client = client;
        _module = module;
    }

    public void UseJobs(IJobQueue jobs)
    {
        _jobs = jobs;
    }

    public void Map(IModuleEndpointBuilder endpoints)
    {
        endpoints.MapGet("models", ListModels);
        endpoints.MapGet("catalog", Catalog);
        endpoints.MapPost("models/pull", Pull);
        endpoints.MapPost("models/load", Load);
        endpoints.MapPost("models/unload", Unload);
        endpoints.MapPost("models/remove", Remove);
        endpoints.MapGet("network", Network);
        endpoints.MapPut("network", ChangeNetwork);
    }

    private IResult Network()
    {
        return Results.Json(_module.NetworkState());
    }

    private IResult ChangeNetwork(ListenBody body)
    {
        if (_jobs is null)
        {
            return Results.Json(new
            {
                failure = new FailureCard("Penelope can't change that yet.", "The restart queue is not available.", []),
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var existing = _jobs.RunningId("ollama", "restart");
        if (existing is not null)
        {
            return Results.Json(new { jobId = existing });
        }

        var expose = body.ExposeOnLan;
        var jobId = _jobs.Start("ollama", "restart", (log, token) => _module.ApplyListenAsync(expose, log, token));
        return Results.Json(new { jobId }, statusCode: StatusCodes.Status202Accepted);
    }

    private async Task<IResult> ListModels(CancellationToken cancellationToken)
    {
        if (await _client.GetVersionAsync(cancellationToken) is null)
        {
            return Stopped();
        }

        var models = await _client.ListModelsAsync(cancellationToken);
        return Results.Json(new { models });
    }

    private IResult Catalog()
    {
        var entries = OllamaCatalog.Load();
        return Results.Json(new
        {
            entries,
            fitKnown = false,
            note = "Penelope is not reading memory yet, so it is not judging which model fits.",
            freeBytes = OllamaCatalog.FreeBytes(),
        });
    }

    private async Task<IResult> Pull(ModelNameBody body, CancellationToken cancellationToken)
    {
        var name = CleanName(body.Name);
        if (name is null)
        {
            return Results.BadRequest(new { message = "Enter a model name such as llama3.2:1b." });
        }

        if (_jobs is null)
        {
            return Results.Json(new
            {
                failure = new FailureCard("Penelope can't download that model yet.", "The download queue is not available.", []),
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var existing = _jobs.RunningId("ollama", "pull");
        if (existing is not null)
        {
            return Results.Json(new { jobId = existing });
        }

        if (await _client.GetVersionAsync(cancellationToken) is null)
        {
            return Stopped();
        }

        var refusal = OllamaCatalog.RefuseIfTooLarge(name, OllamaCatalog.Load());
        if (refusal is not null)
        {
            return Results.Json(new { failure = refusal.Failure }, statusCode: StatusCodes.Status409Conflict);
        }

        var jobId = _jobs.Start("ollama", "pull", (log, token) => _client.PullAsync(name, log, token));
        return Results.Json(new { jobId }, statusCode: StatusCodes.Status202Accepted);
    }

    private Task<IResult> Load(ModelNameBody body, CancellationToken cancellationToken)
    {
        return Memory(body, keepAlive: -1, cancellationToken);
    }

    private Task<IResult> Unload(ModelNameBody body, CancellationToken cancellationToken)
    {
        return Memory(body, keepAlive: 0, cancellationToken);
    }

    private async Task<IResult> Memory(ModelNameBody body, int keepAlive, CancellationToken cancellationToken)
    {
        var name = CleanName(body.Name);
        if (name is null)
        {
            return Results.BadRequest(new { message = "That model name is not valid." });
        }

        if (await _client.GetVersionAsync(cancellationToken) is null)
        {
            return Stopped();
        }

        var result = await _client.GenerateAsync(name, keepAlive, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { ok = true })
            : Results.Json(new { failure = result.Failure }, statusCode: StatusCodes.Status409Conflict);
    }

    private async Task<IResult> Remove(ModelNameBody body, CancellationToken cancellationToken)
    {
        var name = CleanName(body.Name);
        if (name is null)
        {
            return Results.BadRequest(new { message = "That model name is not valid." });
        }

        if (await _client.GetVersionAsync(cancellationToken) is null)
        {
            return Stopped();
        }

        var result = await _client.DeleteAsync(name, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { ok = true })
            : Results.Json(new { failure = result.Failure }, statusCode: StatusCodes.Status409Conflict);
    }

    internal static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 200)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            var ok = char.IsAsciiLetterOrDigit(character) || character is ':' or '.' or '_' or '-' or '/';
            if (!ok)
            {
                return null;
            }
        }

        return trimmed;
    }

    private static IResult Stopped()
    {
        return Results.Json(new
        {
            failure = new FailureCard(
                "Ollama is not running.",
                "Start Ollama, then Penelope can work with models.",
                []),
        }, statusCode: StatusCodes.Status409Conflict);
    }
}

public sealed record ModelNameBody(string? Name);

public sealed record ListenBody(bool ExposeOnLan);
