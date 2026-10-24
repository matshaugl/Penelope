using Penelope.Abstractions;
using Penelope.Infrastructure.Downloads;
using Penelope.Infrastructure.Events;
using Penelope.Infrastructure.Logging;
using Xunit;

namespace Penelope.Host.Tests;

public class InfrastructureTests
{
    [Fact]
    public void Event_bus_notifies_subscribers()
    {
        var bus = new EventBus();
        var seen = 0;
        using var _ = bus.Subscribe(PenelopeEventNames.ToolStarted, _ => seen++);
        bus.Publish(new PenelopeEvent(PenelopeEventNames.ToolStarted, DateTimeOffset.UtcNow, "ollama"));
        Assert.Equal(1, seen);
    }

    [Fact]
    public void Diagnostic_log_keeps_recent_entries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "penelope-tests", Guid.NewGuid().ToString("n"));
        var log = new PenelopeDiagnosticLog(directory);
        log.Write(PenelopeLogLevel.Info, "test", "hello");
        var recent = log.Recent(10);
        Assert.Single(recent);
        Assert.Equal("hello", recent[0].Message);
    }

    [Fact]
    public async Task Download_manager_follows_https_redirect()
    {
        var handler = new RedirectHandler();
        var manager = new DownloadManager(handler);
        var destination = Path.Combine(Path.GetTempPath(), $"penelope-{Guid.NewGuid():n}.bin");
        var log = new CollectingJobLog();
        try
        {
            var result = await manager.DownloadFileAsync(
                new DownloadRequest("https://origin.test/file.bin", destination, ExpectedSizeBytes: 4),
                log,
                CancellationToken.None);
            Assert.True(result.Succeeded);
            Assert.Contains(log.Lines, line => line.Contains("Redirected to", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }
    }

    private sealed class CollectingJobLog : IJobLog
    {
        public List<string> Lines { get; } = [];

        public CancellationToken CancellationToken => CancellationToken.None;

        public void AllowCancel(bool allowed)
        {
        }

        public void Error(string text) => Lines.Add(text);

        public void Info(string text) => Lines.Add(text);

        public void Progress(long completed, long total, string message)
        {
        }

        public void Warning(string text) => Lines.Add(text);
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "origin.test")
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://cdn.test/file.bin") },
                });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("test"u8.ToArray()),
            });
        }
    }
}
