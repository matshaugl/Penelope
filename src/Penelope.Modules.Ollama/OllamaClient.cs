using System.Net.Http.Json;
using System.Text.Json;
using Penelope.Abstractions;

namespace Penelope.Modules.Ollama;

public sealed class OllamaClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public OllamaClient(int port)
        : this(new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = Timeout.InfiniteTimeSpan,
        })
    {
    }

    internal OllamaClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            using var response = await _http.GetAsync("api/version", timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<VersionBody>(Json, timeout.Token);
            return string.IsNullOrWhiteSpace(body?.Version) ? null : body.Version.Trim();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    public async Task UnloadLoadedModelsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.GetAsync("api/ps", timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var body = await response.Content.ReadFromJsonAsync<PsBody>(Json, timeout.Token);
            names = body?.Models?
                .Select(model => model.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToArray() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return;
        }

        foreach (var name in names)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var content = JsonContent.Create(new Dictionary<string, object?>
                {
                    ["model"] = name,
                    ["prompt"] = "",
                    ["stream"] = false,
                    ["keep_alive"] = 0,
                });
                using var response = await _http.PostAsync("api/generate", content, timeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
            }
        }
    }

    public async Task<IReadOnlyList<InstalledModel>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var tagsResponse = await _http.GetAsync("api/tags", timeout.Token);
        tagsResponse.EnsureSuccessStatusCode();
        var tags = await tagsResponse.Content.ReadFromJsonAsync<TagsBody>(Json, timeout.Token);
        HashSet<string> loaded;
        try
        {
            using var psResponse = await _http.GetAsync("api/ps", timeout.Token);
            var ps = psResponse.IsSuccessStatusCode
                ? await psResponse.Content.ReadFromJsonAsync<PsBody>(Json, timeout.Token)
                : null;
            loaded = ps?.Models?
                .Select(model => model.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return tags?.Models?
            .Where(model => !string.IsNullOrWhiteSpace(model.Name))
            .Select(model => new InstalledModel(model.Name!, model.Size, loaded.Contains(model.Name!)))
            .ToArray() ?? [];
    }

    public async Task<ModuleActionResult> PullAsync(string name, IJobLog log, CancellationToken cancellationToken)
    {
        log.AllowCancel(true);
        log.Info($"Downloading {name}.");
        log.Info("Closing Penelope stops this download.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull")
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["model"] = name,
                ["stream"] = true,
            }),
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            log.Error("Ollama refused the download.");
            return FailedDownload("Ollama refused the download.");
        }

        var layers = new Dictionary<string, (long Completed, long Total)>(StringComparer.Ordinal);
        var lastStatus = "";
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            PullLine? progress;
            try
            {
                progress = JsonSerializer.Deserialize<PullLine>(line, Json);
            }
            catch (JsonException)
            {
                continue;
            }

            if (progress is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(progress.Error))
            {
                log.Error(progress.Error);
                return FailedDownload(progress.Error);
            }

            if (!string.IsNullOrWhiteSpace(progress.Status) && progress.Status != lastStatus)
            {
                lastStatus = progress.Status;
                log.Info(progress.Status);
            }

            if (progress.Digest is not null && progress.Total is > 0)
            {
                layers[progress.Digest] = (progress.Completed ?? 0, progress.Total.Value);
                var completed = layers.Values.Sum(item => item.Completed);
                var total = layers.Values.Sum(item => item.Total);
                log.Progress(completed, total, lastStatus.Length == 0 ? "Downloading" : lastStatus);
            }
        }

        log.Progress(1, 1, "Finished.");
        log.Info($"{name} is on this computer.");
        return new ModuleActionResult(true, null);
    }

    public async Task<ModuleActionResult> GenerateAsync(string name, int keepAlive, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["model"] = name,
            ["prompt"] = "",
            ["stream"] = false,
            ["keep_alive"] = keepAlive,
        });
        using var response = await _http.PostAsync("api/generate", content, timeout.Token);
        if (response.IsSuccessStatusCode)
        {
            return new ModuleActionResult(true, null);
        }

        return new ModuleActionResult(false, new FailureCard(
            keepAlive == 0 ? "That model is still in memory." : "That model didn't load.",
            "Ollama didn't finish the request.",
            []));
    }

    public IAsyncEnumerable<ModelEvent> ChatAsync(ModelCompletion request, CancellationToken cancellationToken)
    {
        return OllamaChat.CompleteAsync(_http, request, cancellationToken);
    }

    public async Task<ModuleActionResult> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var content = JsonContent.Create(new Dictionary<string, object?> { ["model"] = name });
        using var response = await _http.PostAsync("api/delete", content, timeout.Token);
        if (response.IsSuccessStatusCode)
        {
            return new ModuleActionResult(true, null);
        }

        return new ModuleActionResult(false, new FailureCard(
            "That model is still on disk.",
            "Ollama didn't remove it.",
            []));
    }

    private static ModuleActionResult FailedDownload(string detail)
    {
        return new ModuleActionResult(false, new FailureCard(
            "The model didn't download.",
            detail,
            [new FailureFix("retry", "Try again", FixKind.Retry, null)]));
    }

    private sealed record VersionBody(string? Version);

    private sealed record PsBody(List<PsModel>? Models);

    private sealed record PsModel(string? Name);

    private sealed record TagsBody(List<TagModel>? Models);

    private sealed record TagModel(string? Name, long Size);

    private sealed record PullLine(string? Status, string? Digest, long? Total, long? Completed, string? Error);
}

public sealed record InstalledModel(string Name, long Size, bool InMemory);
