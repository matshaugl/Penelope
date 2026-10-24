using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Penelope.Host.Plugins;
using Penelope.Host.Security;
using Xunit;

namespace Penelope.Host.Tests;

public class DashboardTests : IClassFixture<PenelopeFactory>
{
    private readonly PenelopeFactory _factory;

    public DashboardTests(PenelopeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void HostProjectDoesNotReferencePlugins()
    {
        var csproj = File.ReadAllText(Path.Combine(Repo.Root, "src", "Penelope.Host", "Penelope.Host.csproj"));
        Assert.DoesNotContain("Penelope.Plugins", csproj);
        Assert.DoesNotContain("Penelope.Modules", csproj);
    }

    [Fact]
    public void ABrokenManifestDoesNotBlockAnotherPlugin()
    {
        var root = Path.Combine(Path.GetTempPath(), "penelope-plugins-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, "broken"));
        File.WriteAllText(Path.Combine(root, "broken", "manifest.json"), """{"id":"future","name":"Future","assembly":"missing.dll","hostApi":99}""");
        CopyDirectory(Path.Combine(Repo.Root, "plugins", "sample"), Path.Combine(root, "sample"));

        var catalog = PluginCatalog.Load(root, LoggerFactory.Create(_ => { }).CreateLogger("test"));
        var future = catalog.Find("future");
        var sample = catalog.Find("sample");

        Assert.NotNull(future);
        Assert.Equal("This tool needs a newer Penelope.", future!.LoadError);
        Assert.NotNull(sample?.Module);
        Assert.Equal("sample", sample!.Module!.Id);
    }

    [Theory]
    [InlineData("\"1.0\"", null)]
    [InlineData("1", null)]
    [InlineData("\"1.1\"", "This tool needs a newer Penelope.")]
    [InlineData("\"2.0\"", "This tool needs a newer Penelope.")]
    [InlineData("\"0.9\"", "This tool was built for an older Penelope.")]
    [InlineData("\"one\"", "This tool couldn't be loaded.")]
    public void HostApiVersionDecidesWhetherAPluginLoads(string hostApi, string? expectedError)
    {
        var root = Path.Combine(Path.GetTempPath(), "penelope-plugins-" + Guid.NewGuid().ToString("n"));
        CopyDirectory(Path.Combine(Repo.Root, "plugins", "sample"), Path.Combine(root, "sample"));
        var manifestPath = Path.Combine(root, "sample", "manifest.json");
        var manifest = File.ReadAllText(manifestPath).Replace("\"hostApi\": \"1.0\"", $"\"hostApi\": {hostApi}");
        File.WriteAllText(manifestPath, manifest);

        var sample = PluginCatalog.Load(root, LoggerFactory.Create(_ => { }).CreateLogger("test")).Find("sample");

        Assert.NotNull(sample);
        Assert.Equal(expectedError, sample!.LoadError);
    }

    [Fact]
    public async Task MutatingCallWithoutSessionIsRejected()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/modules/sample/start", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task CrossOriginCallIsRejected()
    {
        var client = await AuthedClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/modules/sample/start");
        request.Headers.Add("Origin", "https://evil.example");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SamplePluginIsVisibleAndInstallShowsAFailure()
    {
        var client = await AuthedClient();
        var modules = await client.GetFromJsonAsync<JsonElement>("/api/modules");
        Assert.Contains(modules.EnumerateArray(), module => module.GetProperty("id").GetString() == "sample");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/modules/sample/install");
        request.Headers.Add("Origin", Origin(client));
        var started = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var body = await started.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = body.GetProperty("jobId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        JsonElement job = default;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var snapshot = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
            if (snapshot.GetProperty("state").GetString() is "failed" or "completed" or "cancelled")
            {
                job = snapshot;
                break;
            }

            await Task.Delay(50);
        }

        Assert.Equal("failed", job.GetProperty("state").GetString());
        Assert.Equal("Sample tool didn't install.", job.GetProperty("failure").GetProperty("summary").GetString());
        Assert.Contains(job.GetProperty("log").EnumerateArray(), line => line.GetProperty("level").GetString() == "error");
        Assert.Equal("retry", job.GetProperty("failure").GetProperty("fixes")[0].GetProperty("kind").GetString());
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

    private static string Origin(HttpClient client)
    {
        return client.BaseAddress!.GetLeftPart(UriPartial.Authority);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }
}

public sealed class PenelopeFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Penelope:Testing", "true");
        builder.UseSetting("Penelope:NoBrowser", "true");
        builder.UseSetting("Penelope:NoTray", "true");
        builder.UseSetting("Penelope:Plugins", Path.Combine(Repo.Root, "plugins"));
        builder.UseSetting("Penelope:ConfigPath", Path.Combine(Path.GetTempPath(), "penelope-config-" + Guid.NewGuid().ToString("n") + ".json"));
    }
}

static class Repo
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Penelope.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Couldn't find Penelope.sln");
    }
}
