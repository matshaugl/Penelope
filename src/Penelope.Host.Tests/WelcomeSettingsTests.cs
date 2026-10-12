using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Penelope.Host.Security;
using Penelope.Host.Settings;
using Xunit;

namespace Penelope.Host.Tests;

public class WelcomeSettingsTests : IClassFixture<PenelopeFactory>
{
    private readonly PenelopeFactory _factory;

    public WelcomeSettingsTests(PenelopeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void A_host_executable_path_is_quoted_and_dotnet_is_not()
    {
        var folder = Path.Combine(Path.GetTempPath(), "penelope-startup-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "Penelope.Host.exe");
        File.WriteAllText(exe, "");

        Assert.Equal($"\"{exe}\"", SignInStartup.QuoteHostPath(exe, folder));
        Assert.Equal($"\"{exe}\"", SignInStartup.QuoteHostPath(Path.Combine(folder, "dotnet.exe"), folder));

        Directory.Delete(folder, recursive: true);
        Assert.Null(SignInStartup.QuoteHostPath(Path.Combine(folder, "dotnet.exe"), folder));
    }

    [Fact]
    public void Memory_mode_does_not_need_the_registry()
    {
        var startup = new SignInStartup(memoryOnly: true);
        Assert.False(startup.IsEnabled());
        Assert.True(startup.Set(true));
        Assert.True(startup.IsEnabled());
        Assert.True(startup.Set(false));
        Assert.False(startup.IsEnabled());
    }

    [Fact]
    public async Task The_welcome_flag_and_sign_in_switch_round_trip()
    {
        var client = await AuthedClient();
        var origin = client.BaseAddress!.GetLeftPart(UriPartial.Authority);

        var before = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.False(before.GetProperty("setupComplete").GetBoolean());
        Assert.False(before.GetProperty("startOnSignIn").GetBoolean());

        var completed = await Put(client, origin, new { setupComplete = true });
        Assert.True(completed.GetProperty("setupComplete").GetBoolean());

        var enabled = await Put(client, origin, new { startOnSignIn = true });
        Assert.True(enabled.GetProperty("startOnSignIn").GetBoolean());

        var disabled = await Put(client, origin, new { startOnSignIn = false });
        Assert.False(disabled.GetProperty("startOnSignIn").GetBoolean());
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

    private static async Task<JsonElement> Put(HttpClient client, string origin, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Origin", origin);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
