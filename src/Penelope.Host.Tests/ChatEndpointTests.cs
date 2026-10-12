using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Penelope.Host.Security;
using Xunit;

namespace Penelope.Host.Tests;

public class ChatEndpointTests : IClassFixture<PenelopeFactory>
{
    private readonly PenelopeFactory _factory;

    public ChatEndpointTests(PenelopeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_session_lists_web_search_and_requires_the_cookie()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/chat/session")).StatusCode);

        var client = await AuthedClient();
        var session = await client.GetFromJsonAsync<JsonElement>("/api/chat/session");
        Assert.Equal("Ollama", session.GetProperty("provider").GetString());
        Assert.Contains("web_search", session.GetProperty("tools").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task A_chat_post_needs_the_page_origin_and_a_message()
    {
        var client = await AuthedClient();
        var origin = client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        var missing = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new { messages = new[] { new { role = "user", content = "Hi" } } }),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(missing)).StatusCode);

        var empty = new HttpRequestMessage(HttpMethod.Post, "/api/agent/stream")
        {
            Content = new StringContent("""{"messages":[]}""", Encoding.UTF8, "application/json"),
        };
        empty.Headers.Add("Origin", origin);
        var response = await client.SendAsync(empty);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Enter a message.", body.GetProperty("message").GetString());
    }

    private async Task<HttpClient> AuthedClient()
    {
        var token = _factory.Services.GetRequiredService<SessionToken>().Value;
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var session = await client.GetAsync($"/session?token={token}");
        Assert.Equal(HttpStatusCode.Redirect, session.StatusCode);
        return client;
    }
}
