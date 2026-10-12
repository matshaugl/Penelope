using System.Text.Json;
using Penelope.Abstractions;

namespace Penelope.Host.Agent;

interface IWebSearch
{
    Task<IReadOnlyList<WebHit>> SearchAsync(string query, CancellationToken cancellationToken);
}

sealed record WebHit(string Title, string Url, string Snippet);

sealed class WebSearchTool : IAgentTool
{
    private readonly IWebSearch _search;

    public WebSearchTool(IWebSearch search)
    {
        _search = search;
    }

    public string Name => "web_search";

    public string Description =>
        "Look up a short public web query. Input is a JSON object with a query string. Returns titles, links, and short snippets. It does not open the links.";

    public string ParametersSchema =>
        """{"type":"object","properties":{"query":{"type":"string","description":"Words to look up"}},"required":["query"]}""";

    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var query = ReadQuery(argumentsJson);
        if (query is null)
        {
            return "Enter a short search.";
        }

        try
        {
            var hits = await _search.SearchAsync(query, cancellationToken);
            if (hits.Count == 0)
            {
                return "No web results for that query.";
            }

            var lines = new List<string>();
            var number = 1;
            foreach (var hit in hits.Take(5))
            {
                lines.Add($"{number}. {hit.Title}");
                lines.Add(hit.Url);
                if (!string.IsNullOrWhiteSpace(hit.Snippet))
                {
                    lines.Add(hit.Snippet);
                }

                number++;
            }

            var text = string.Join('\n', lines);
            return text.Length > 2000 ? text[..2000] : text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return "Web search didn't answer. This computer may be offline.";
        }
    }

    internal static string? ReadQuery(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("query", out var query)
                || query.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = query.GetString() ?? "";
            var parts = text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var cleaned = string.Join(' ', parts);
            if (cleaned.Length == 0)
            {
                return null;
            }

            return cleaned.Length > 200 ? cleaned[..200] : cleaned;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

sealed class WebSearchToolSource : IAgentToolSource
{
    public WebSearchToolSource(IWebSearch search)
    {
        Tools = [new WebSearchTool(search)];
    }

    public IReadOnlyList<IAgentTool> Tools { get; }
}
