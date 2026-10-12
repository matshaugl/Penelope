using System.Net;
using System.Security.Cryptography;
using System.Text;
using Penelope.Abstractions;
using Penelope.Modules.Ollama;
using Xunit;

namespace Penelope.Host.Tests;

public class OllamaDownloadTests
{
    [Fact]
    public async Task A_directory_prefixed_checksum_matches_the_downloaded_installer()
    {
        var body = "penelope-installer"u8.ToArray();
        var hash = Hash(body);
        await using var fixture = Fixture.Start(body, hash, $"{hash}  ./OllamaSetup.exe\n");
        var log = new ListLog();

        var result = await fixture.Setup.DownloadAndVerifyAsync(log, CancellationToken.None);

        Assert.Equal(SetupProblem.None, result.Problem);
        Assert.Equal("0.40.3", result.Version);
        Assert.Contains(log.Lines, line => line.StartsWith("Download URL https://downloads.test/OllamaSetup.exe", StringComparison.Ordinal));
        Assert.Contains(log.Lines, line => line.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Contains(log.Lines, line => line.StartsWith("Final URL https://downloads.test/OllamaSetup.exe", StringComparison.Ordinal));
        Assert.Contains(log.Lines, line => line == $"Expected checksum {hash}");
        Assert.Contains(log.Lines, line => line == $"Calculated checksum {hash}");
        Assert.Contains(log.Lines, line => line == $"File size {body.Length}");
    }

    [Fact]
    public async Task A_redirect_is_followed_and_recorded()
    {
        var body = "redirected"u8.ToArray();
        var hash = Hash(body);
        await using var fixture = Fixture.Start(body, hash, $"{hash}  ./OllamaSetup.exe\n", redirect: true);
        var log = new ListLog();

        var result = await fixture.Setup.DownloadAndVerifyAsync(log, CancellationToken.None);

        Assert.Equal(SetupProblem.None, result.Problem);
        Assert.Contains(log.Lines, line => line.Contains("HTTP 302", StringComparison.Ordinal));
        Assert.Contains(log.Lines, line => line == "Redirected to https://cdn.test/OllamaSetup.exe");
        Assert.Contains(log.Lines, line => line == "Final URL https://cdn.test/OllamaSetup.exe");
    }

    [Fact]
    public async Task A_failed_checksum_keeps_the_file_and_shows_both_values()
    {
        var body = "not-the-published-bytes"u8.ToArray();
        var published = new string('a', 64);
        await using var fixture = Fixture.Start(body, published, $"{published}  ./OllamaSetup.exe\n");
        var log = new ListLog();

        var result = await fixture.Setup.DownloadAndVerifyAsync(log, CancellationToken.None);

        Assert.Equal(SetupProblem.Checksum, result.Problem);
        Assert.Contains($"Expected {published}.", result.Detail);
        Assert.Contains($"Calculated {Hash(body)}.", result.Detail);
        Assert.Contains(fixture.InstallerPath, result.Detail);
        Assert.True(File.Exists(fixture.InstallerPath));
        Assert.Equal(body, await File.ReadAllBytesAsync(fixture.InstallerPath));
    }

    [Fact]
    public async Task Disagreeing_upstream_checksums_stop_the_download()
    {
        var body = "untouched"u8.ToArray();
        var asset = Hash(body);
        var other = new string('b', 64);
        await using var fixture = Fixture.Start(body, asset, $"{other}  ./OllamaSetup.exe\n");
        var log = new ListLog();

        var result = await fixture.Setup.DownloadAndVerifyAsync(log, CancellationToken.None);

        Assert.Equal(SetupProblem.Checksum, result.Problem);
        Assert.Contains(asset, result.Detail);
        Assert.Contains(other, result.Detail);
        Assert.Equal(0, fixture.Handler.InstallerRequests);
        Assert.False(File.Exists(fixture.InstallerPath));
    }

    [Fact]
    public async Task A_short_download_is_retried_until_the_file_is_complete()
    {
        var body = "complete-file"u8.ToArray();
        var hash = Hash(body);
        await using var fixture = Fixture.Start(body, hash, $"{hash}  ./OllamaSetup.exe\n", truncateOnce: true);
        var log = new ListLog();

        var result = await fixture.Setup.DownloadAndVerifyAsync(log, CancellationToken.None);

        Assert.Equal(SetupProblem.None, result.Problem);
        Assert.Equal(2, fixture.Handler.InstallerRequests);
        Assert.Contains(log.Lines, line => line.Contains("Download attempt 1 of 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_status_is_retried()
    {
        var body = "after-retry"u8.ToArray();
        var hash = Hash(body);
        await using var fixture = Fixture.Start(body, hash, $"{hash}  ./OllamaSetup.exe\n", failuresBeforeSuccess: 1);
        var log = new ListLog();

        var result = await fixture.Setup.DownloadAndVerifyAsync(log, CancellationToken.None);

        Assert.Equal(SetupProblem.None, result.Problem);
        Assert.Equal(2, fixture.Handler.InstallerRequests);
        Assert.Contains(log.Lines, line => line.Contains("HTTP 503", StringComparison.Ordinal));
    }

    private static string Hash(byte[] body)
    {
        return Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string folder, ScriptHandler handler, GitHubOllamaSetup setup)
        {
            Folder = folder;
            Handler = handler;
            Setup = setup;
        }

        public string Folder { get; }

        public ScriptHandler Handler { get; }

        public GitHubOllamaSetup Setup { get; }

        public string InstallerPath => Path.Combine(Folder, "OllamaSetup.exe");

        public static Fixture Start(
            byte[] body,
            string assetHash,
            string checksumText,
            bool redirect = false,
            bool truncateOnce = false,
            int failuresBeforeSuccess = 0)
        {
            var folder = Path.Combine(Path.GetTempPath(), "penelope-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var handler = new ScriptHandler(body, assetHash, checksumText, redirect, truncateOnce, failuresBeforeSuccess);
            var setup = new GitHubOllamaSetup(handler, folder);
            return new Fixture(folder, handler, setup);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        private readonly string _assetHash;
        private readonly string _checksumText;
        private readonly bool _redirect;
        private readonly bool _truncateOnce;
        private readonly int _failuresBeforeSuccess;

        public ScriptHandler(byte[] body, string assetHash, string checksumText, bool redirect, bool truncateOnce, int failuresBeforeSuccess)
        {
            _body = body;
            _assetHash = assetHash;
            _checksumText = checksumText;
            _redirect = redirect;
            _truncateOnce = truncateOnce;
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int InstallerRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            if (url.Contains("/releases/latest", StringComparison.Ordinal))
            {
                var json = "{"
                    + "\"tag_name\":\"v0.40.3\","
                    + "\"assets\":["
                    + "{\"name\":\"OllamaSetup.exe\",\"browser_download_url\":\"https://downloads.test/OllamaSetup.exe\",\"size\":" + _body.Length + ",\"digest\":\"sha256:" + _assetHash + "\"},"
                    + "{\"name\":\"sha256sum.txt\",\"browser_download_url\":\"https://downloads.test/sha256sum.txt\",\"size\":80}"
                    + "]}";
                return Task.FromResult(Text(json, "application/json"));
            }

            if (url.Contains("sha256sum.txt", StringComparison.Ordinal))
            {
                return Task.FromResult(Text(_checksumText, "text/plain"));
            }

            if (url == "https://downloads.test/OllamaSetup.exe" && _redirect)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("https://cdn.test/OllamaSetup.exe");
                return Task.FromResult(redirect);
            }

            if (url is "https://downloads.test/OllamaSetup.exe" or "https://cdn.test/OllamaSetup.exe")
            {
                InstallerRequests++;
                if (InstallerRequests <= _failuresBeforeSuccess)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                if (_truncateOnce && InstallerRequests == _failuresBeforeSuccess + 1)
                {
                    var shortBody = _body.Length == 0 ? _body : _body[..^1];
                    var incomplete = new ByteArrayContent(shortBody);
                    incomplete.Headers.ContentLength = _body.Length;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = incomplete });
                }

                var complete = new ByteArrayContent(_body);
                complete.Headers.ContentLength = _body.Length;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = complete });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Text(string text, string type)
        {
            var content = new StringContent(text, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

    private sealed class ListLog : IJobLog
    {
        public List<string> Lines { get; } = [];

        public CancellationToken CancellationToken => CancellationToken.None;

        public void Info(string text) => Lines.Add(text);

        public void Warning(string text) => Lines.Add(text);

        public void Error(string text) => Lines.Add(text);

        public void Progress(long completed, long total, string message)
        {
        }

        public void AllowCancel(bool allowed)
        {
        }
    }
}
