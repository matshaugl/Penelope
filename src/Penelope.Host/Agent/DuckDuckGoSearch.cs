using System.Net;
using System.Text.RegularExpressions;

namespace Penelope.Host.Agent;

sealed class DuckDuckGoSearch : IWebSearch
{
    private readonly HttpClient _http;

    public DuckDuckGoSearch()
        : this(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
        })
    {
    }

    internal DuckDuckGoSearch(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<WebHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var next = new Uri("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query));
        for (var hop = 0; hop < 4; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    throw new HttpRequestException("Search did not stay on DuckDuckGo.");
                }

                if (!location.IsAbsoluteUri)
                {
                    location = new Uri(next, location);
                }

                if (!IsDuckDuckGo(location))
                {
                    throw new HttpRequestException("Search did not stay on DuckDuckGo.");
                }

                next = location;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Search returned {(int)response.StatusCode}.");
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (html.Length > 1_000_000)
            {
                html = html[..1_000_000];
            }

            return DuckDuckGoHtml.Parse(html);
        }

        throw new HttpRequestException("Search did not stay on DuckDuckGo.");
    }

    private static bool IsDuckDuckGo(Uri uri)
    {
        return uri.Host.Equals("duckduckgo.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".duckduckgo.com", StringComparison.OrdinalIgnoreCase);
    }
}

static partial class DuckDuckGoHtml
{
    public static IReadOnlyList<WebHit> Parse(string html)
    {
        var titles = new List<(string Title, string Url)>();
        var snippets = new List<string>();
        foreach (Match match in Anchor().Matches(html))
        {
            var attrs = match.Groups["attrs"].Value;
            var text = Clean(match.Groups["text"].Value);
            if (HasClass(attrs, "result__a"))
            {
                var url = PublicUrl(Attr(attrs, "href"));
                if (url is null || text.Length == 0 || titles.Any(item => item.Url == url))
                {
                    continue;
                }

                titles.Add((text, url));
            }
            else if (HasClass(attrs, "result__snippet") && text.Length > 0)
            {
                snippets.Add(text.Length > 240 ? text[..240] : text);
            }
        }

        var hits = new List<WebHit>();
        for (var i = 0; i < titles.Count && hits.Count < 5; i++)
        {
            var snippet = i < snippets.Count ? snippets[i] : "";
            hits.Add(new WebHit(titles[i].Title, titles[i].Url, snippet));
        }

        return hits;
    }

    private static bool HasClass(string attrs, string className)
    {
        var match = ClassAttr().Match(attrs);
        if (!match.Success)
        {
            return false;
        }

        return match.Groups["class"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Attr(string attrs, string name)
    {
        var match = Regex.Match(attrs, name + "=\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    private static string? PublicUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            href = "https:" + href;
        }

        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Host.Equals("duckduckgo.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            var targetText = Query(uri, "uddg");
            if (targetText is null || !Uri.TryCreate(targetText, UriKind.Absolute, out var target))
            {
                return null;
            }

            uri = target;
        }

        if (uri.Scheme is not ("https" or "http"))
        {
            return null;
        }

        return uri.ToString();
    }

    private static string? Query(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(part[..split]);
            if (!name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(part[(split + 1)..]);
        }

        return null;
    }

    private static string Clean(string text)
    {
        var decoded = WebUtility.HtmlDecode(Tag().Replace(text, " "));
        return Space().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex("<a\\b(?<attrs>[^>]*)>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Anchor();

    [GeneratedRegex("class=\"(?<class>[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ClassAttr();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tag();

    [GeneratedRegex("\\s+")]
    private static partial Regex Space();
}
