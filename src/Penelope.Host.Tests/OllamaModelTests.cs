using System.Net;
using System.Text;
using Penelope.Abstractions;
using Penelope.Modules.Ollama;
using Xunit;

namespace Penelope.Host.Tests;

public class OllamaModelTests
{
    [Fact]
    public void ModelNamesRejectSpacesAndCommands()
    {
        Assert.Equal("llama3.2:1b", OllamaApi.CleanName(" llama3.2:1b "));
        Assert.Null(OllamaApi.CleanName("llama3.2:1b && calc"));
        Assert.Null(OllamaApi.CleanName(""));
    }

    [Fact]
    public async Task PullStreamsProgressIntoTheJob()
    {
        await using var server = await PullServer.Start();
        var client = new OllamaClient(server.Port);
        var log = new CollectingLog();

        var result = await client.PullAsync("qwen2.5:0.5b", log, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(log.Lines, line => line.Contains("pulling", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(log.Marks, item => item.Total == 100 && item.Completed == 40);
    }

    [Fact]
    public async Task PullCancelStopsTheStream()
    {
        await using var server = await PullServer.Start(hold: true);
        var client = new OllamaClient(server.Port);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PullAsync("qwen2.5:0.5b", new CollectingLog(), cancel.Token));
    }

    [Fact]
    public void CatalogShipsTheSmallStarter()
    {
        var entries = OllamaCatalog.Load();
        Assert.Contains(entries, entry => entry.Tag == "qwen2.5:0.5b" && entry.Role == "starter-tiny");
    }

    private sealed class CollectingLog : IJobLog
    {
        public CancellationToken CancellationToken => CancellationToken.None;

        public List<string> Lines { get; } = [];

        public List<(long Completed, long Total)> Marks { get; } = [];

        public void Info(string text) => Lines.Add(text);

        public void Warning(string text) => Lines.Add(text);

        public void Error(string text) => Lines.Add(text);

        public void Progress(long completed, long total, string message)
        {
            Marks.Add((completed, total));
            Lines.Add(message);
        }

        public void AllowCancel(bool allowed)
        {
        }
    }

    private sealed class PullServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cancel = new();

        private PullServer(int port, bool hold)
        {
            Port = port;
            Hold = hold;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        public int Port { get; }

        private bool Hold { get; }

        public static async Task<PullServer> Start(bool hold = false)
        {
            var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            var server = new PullServer(port, hold);
            await Task.Delay(50);
            return server;
        }

        public ValueTask DisposeAsync()
        {
            _cancel.Cancel();
            _listener.Close();
            return ValueTask.CompletedTask;
        }

        private async Task Loop()
        {
            while (!_cancel.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                var path = context.Request.Url?.AbsolutePath ?? "";
                if (path.EndsWith("/api/pull", StringComparison.Ordinal))
                {
                    context.Response.ContentType = "application/x-ndjson";
                    context.Response.SendChunked = true;
                    var body = Encoding.UTF8.GetBytes("{\"status\":\"pulling manifest\"}\n{\"status\":\"downloading\",\"digest\":\"abc\",\"total\":100,\"completed\":40}\n{\"status\":\"success\"}\n");
                    if (Hold)
                    {
                        await context.Response.OutputStream.WriteAsync(body.AsMemory(0, 10));
                        await context.Response.OutputStream.FlushAsync();
                        try
                        {
                            await Task.Delay(Timeout.Infinite, _cancel.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }

                    await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                    continue;
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
    }
}
