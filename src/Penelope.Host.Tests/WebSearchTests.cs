using System.Net;
using Penelope.Host.Agent;
using Xunit;

namespace Penelope.Host.Tests;

public class WebSearchTests
{
    [Fact]
    public void The_html_parser_reads_titles_links_and_snippets()
    {
        const string html = """
            <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fguide&amp;rut=abc">Example guide</a>
            <a class="result__snippet" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fguide">A short explanation.</a>
            <a class="result__a" href="javascript:alert(1)">Bad</a>
            <a class="result__a" href="https://example.com/plain">Plain page</a>
            """;

        var hits = DuckDuckGoHtml.Parse(html);

        Assert.Equal(2, hits.Count);
        Assert.Equal("Example guide", hits[0].Title);
        Assert.Equal("https://example.com/guide", hits[0].Url);
        Assert.Equal("A short explanation.", hits[0].Snippet);
        Assert.Equal("https://example.com/plain", hits[1].Url);
    }

    [Fact]
    public async Task Search_refuses_a_redirect_off_duckduckgo()
    {
        var handler = new RedirectHandler();
        var search = new DuckDuckGoSearch(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => search.SearchAsync("penelope", CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        Assert.Equal("html.duckduckgo.com", handler.Hosts[0]);
    }

    [Fact]
    public async Task The_tool_formats_hits_and_rejects_a_missing_query()
    {
        var tool = new WebSearchTool(new FakeSearch());
        var text = await tool.InvokeAsync("""{"query":"local models"}""", CancellationToken.None);
        Assert.Contains("Example guide", text);
        Assert.Contains("https://example.com/guide", text);
        Assert.Equal("Enter a short search.", await tool.InvokeAsync("{}", CancellationToken.None));
    }

    private sealed class FakeSearch : IWebSearch
    {
        public Task<IReadOnlyList<WebHit>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Assert.Equal("local models", query);
            return Task.FromResult<IReadOnlyList<WebHit>>([new WebHit("Example guide", "https://example.com/guide", "Snippet")]);
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Hosts.Add(request.RequestUri!.Host);
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.example/steal");
            return Task.FromResult(response);
        }
    }
}
