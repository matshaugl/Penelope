using System.Net;
using System.Text;
using Penelope.Modules.Ollama;
using Xunit;

namespace Penelope.Host.Tests;

public class OllamaModuleTests
{
    [Fact]
    public async Task AlreadyRunningDoesNotLaunchASecondServer()
    {
        await using var server = FakeOllama.Start("0.35.1");
        var process = new RecordingProcess();
        var module = Module(server, process);

        var result = await module.StartAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, process.Launches);
        Assert.Equal("Ollama is running", module.GetStatus().Headline);
        Assert.Equal("0.35.1", module.GetRelease().InstalledVersion);
    }

    [Fact]
    public async Task StartLaunchesTheTrayAndWaitsForTheApi()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.35.1"),
        };
        process.OnLaunch = () => server.Version = "0.35.1";
        var module = Module(server, process);

        var result = await module.StartAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, process.Launches);
        Assert.False(process.LastServe);
        Assert.Equal(@"C:\Ollama\ollama app.exe", process.LastPath);
        Assert.Equal("Ollama is running", module.GetStatus().Headline);
    }

    [Fact]
    public async Task StartUsesServeWhenTheTrayProgramIsMissing()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", null, "0.35.1"),
        };
        process.OnLaunch = () => server.Version = "0.35.1";
        var module = Module(server, process);

        var result = await module.StartAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(process.LastServe);
        Assert.Equal(@"C:\Ollama\ollama.exe", process.LastPath);
    }

    [Fact]
    public async Task MissingOllamaDoesNotLaunchAnything()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess { Location = new OllamaLocation(null, null, null) };
        var module = Module(server, process);

        var result = await module.StartAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Ollama is not installed.", result.Failure?.Summary);
        Assert.Equal(0, process.Launches);
        Assert.Equal("Ollama is not installed", module.GetStatus().Headline);
        Assert.Equal("Use Install Ollama on this page. Penelope downloads the official installer.", result.Failure?.Detail);
    }

    [Fact]
    public async Task AnExistingInstallIsLeftInPlace()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.35.1"),
        };
        var setup = new ScriptedSetup();
        var module = Module(server, process, setup);

        var result = await module.InstallAsync(new CollectingLog(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, setup.Installs);
        Assert.Contains("Already here", module.PlanInstall().Steps.Select(step => step.Label));
    }

    [Fact]
    public async Task AMissingInstallDownloadsAndThenShowsTheInstalledVersion()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess { Location = new OllamaLocation(null, null, null) };
        var setup = new ScriptedSetup();
        setup.BeforeReturn = () => process.Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.36.0");
        var module = Module(server, process, setup);

        Assert.Contains("Download", module.PlanInstall().Steps.Select(step => step.Label));

        var result = await module.InstallAsync(new CollectingLog(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, setup.Installs);
        Assert.Equal("Ollama is stopped", module.GetStatus().Headline);
        Assert.Equal("0.36.0", module.GetRelease().InstalledVersion);
    }

    [Fact]
    public async Task ARefusedInstallerOffersTryAgainAndTheDownloadPage()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess { Location = new OllamaLocation(null, null, null) };
        var setup = new ScriptedSetup { Problem = SetupProblem.Cancelled };
        var module = Module(server, process, setup);

        var result = await module.InstallAsync(new CollectingLog(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Windows asked for permission and the request was cancelled.", result.Failure?.Detail);
        Assert.Contains(result.Failure!.Fixes, fix => fix.Kind == Penelope.Abstractions.FixKind.Retry);
        Assert.Contains(result.Failure.Fixes, fix => fix.Url == "https://ollama.com/download");
        Assert.Equal("Ollama is not installed", module.GetStatus().Headline);
    }

    [Fact]
    public async Task AChecksumMismatchDoesNotClaimTheInstallSucceeded()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess { Location = new OllamaLocation(null, null, null) };
        var setup = new ScriptedSetup { Problem = SetupProblem.Checksum };
        var module = Module(server, process, setup);

        var result = await module.InstallAsync(new CollectingLog(), CancellationToken.None);

        Assert.Equal("The downloaded installer didn't match the published checksum.", result.Failure?.Detail);
    }

    [Fact]
    public async Task AChecksumMismatchShowsTheExpectedAndCalculatedValues()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess { Location = new OllamaLocation(null, null, null) };
        var setup = new ScriptedSetup
        {
            Problem = SetupProblem.Checksum,
            Detail = "The downloaded installer didn't match the published checksum. Expected abc. Calculated def. The file is still at C:\\Penelope\\downloads\\OllamaSetup.exe.",
        };
        var module = Module(server, process, setup);

        var result = await module.InstallAsync(new CollectingLog(), CancellationToken.None);

        Assert.Contains("Expected abc.", result.Failure?.Detail);
        Assert.Contains("Calculated def.", result.Failure?.Detail);
        Assert.Contains("OllamaSetup.exe", result.Failure?.Detail);
        Assert.Single(result.Failure!.Fixes);
        Assert.Equal(Penelope.Abstractions.FixKind.Retry, result.Failure.Fixes[0].Kind);
    }

    [Fact]
    public async Task AFullDiskOffersTheDrive()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess { Location = new OllamaLocation(null, null, null) };
        var module = Module(server, process, new ScriptedSetup { Problem = SetupProblem.Disk });

        var result = await module.InstallAsync(new CollectingLog(), CancellationToken.None);

        Assert.Equal("The models disk does not have room for this download.", result.Failure?.Detail);
        Assert.Equal(Penelope.Abstractions.FixKind.OpenDisk, Assert.Single(result.Failure!.Fixes).Kind);
    }

    [Fact]
    public async Task UpdateDownloadsOnlyWhenTheReleaseIsNewer()
    {
        await using var server = FakeOllama.Start(null);
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.35.1"),
        };
        var same = new ScriptedSetup { Latest = "0.35.1" };
        var current = Module(server, process, same);

        var unchanged = await current.UpgradeAsync(new CollectingLog(), CancellationToken.None);

        Assert.True(unchanged.Succeeded);
        Assert.Equal(0, same.Installs);

        var setup = new ScriptedSetup { Latest = "0.36.0" };
        setup.BeforeReturn = () => process.Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.36.0");
        var module = Module(server, process, setup);

        var updated = await module.UpgradeAsync(new CollectingLog(), CancellationToken.None);

        Assert.True(updated.Succeeded);
        Assert.Equal(1, setup.Installs);
        Assert.Equal("0.36.0", module.GetRelease().InstalledVersion);
        Assert.False(module.GetRelease().UpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateMarksANewerRelease()
    {
        await using var server = FakeOllama.Start("0.35.1");
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.35.1"),
        };
        var module = Module(server, process, new ScriptedSetup { Latest = "0.36.0" });

        await module.CheckForUpdateAsync(CancellationToken.None);

        var release = module.GetRelease();
        Assert.Equal("0.35.1", release.InstalledVersion);
        Assert.Equal("0.36.0", release.LatestVersion);
        Assert.True(release.UpdateAvailable);
        Assert.Null(release.CheckError);
    }

    [Fact]
    public async Task ARunningServerIsStoppedBeforeUpdateAndAFailedStopStaysOnThePage()
    {
        await using var server = FakeOllama.Start("0.35.1");
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.35.1"),
        };
        var setup = new ScriptedSetup { Latest = "0.36.0" };
        var module = Module(server, process, setup);

        var result = await module.UpgradeAsync(new CollectingLog(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Ollama is open and the installer couldn't replace it.", result.Failure?.Detail);
        Assert.Equal("Stop Ollama and try again", result.Failure!.Fixes[0].Label);
        Assert.Equal(0, setup.Installs);
    }

    [Fact]
    public async Task StopUnloadsModelsAndClosesTheListener()
    {
        await using var server = FakeOllama.Start("0.35.1");
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.35.1"),
            Listener = 42,
        };
        process.OnStop = () => server.Version = null;
        var module = Module(server, process);

        var result = await module.StopAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("example:latest", server.Unloaded);
        Assert.Contains(42, process.Stopped);
        Assert.Equal("Ollama is stopped", module.GetStatus().Headline);
        Assert.Equal("0.35.1", module.GetRelease().InstalledVersion);
    }

    [Fact]
    public void ListenerPidMatchesASocketOpenedByThisProcess()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.Equal(Environment.ProcessId, new OllamaProcess().ListenerPid(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void HostProjectDoesNotReferenceTheOllamaModule()
    {
        var csproj = File.ReadAllText(Path.Combine(Repo.Root, "src", "Penelope.Host", "Penelope.Host.csproj"));
        Assert.DoesNotContain("Penelope.Modules.Ollama", csproj);
    }

    [Fact]
    public async Task OpeningTheNetworkStartsServeOnEveryInterfaceAndKeepsTheDashboardAlone()
    {
        await using var server = FakeOllama.Start("0.35.1");
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.35.1"),
            Listener = 4,
        };
        process.OnStop = () => server.Version = null;
        process.OnLaunch = () => server.Version = "0.35.1";
        var firewall = new QuietFirewall();
        string? saved = null;
        var module = new OllamaModule(
            server.Port,
            new OllamaClient(server.Port),
            process,
            TimeSpan.FromSeconds(3),
            firewall,
            host => saved = host,
            () => saved,
            () => ["0.0.0.0"]);

        var result = await module.ApplyListenAsync(true, new CollectingLog(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(process.LastServe);
        Assert.Equal(@"C:\Ollama\ollama.exe", process.LastPath);
        Assert.Equal($"0.0.0.0:{server.Port}", process.LastHost);
        Assert.Equal($"0.0.0.0:{server.Port}", saved);
        Assert.True(firewall.LastAllow);
        Assert.True(module.NetworkState().ExposeOnLan);
        var program = File.ReadAllText(Path.Combine(FindRepo(), "src", "Penelope.Host", "Program.cs"));
        Assert.Contains("127.0.0.1", program, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingTheNetworkRestartsOnLoopbackEvenWhenTheFirewallPromptIsRefused()
    {
        await using var server = FakeOllama.Start("0.35.1");
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", @"C:\Ollama\ollama app.exe", "0.35.1"),
        };
        process.OnStop = () => server.Version = null;
        process.OnLaunch = () => server.Version = "0.35.1";
        var firewall = new QuietFirewall { Next = FirewallStep.Cancelled };
        var module = new OllamaModule(
            server.Port,
            new OllamaClient(server.Port),
            process,
            TimeSpan.FromSeconds(3),
            firewall,
            _ => { },
            () => null,
            () => ["127.0.0.1"]);

        var result = await module.ApplyListenAsync(false, new CollectingLog(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal($"127.0.0.1:{server.Port}", process.LastHost);
        Assert.False(firewall.LastAllow);
        Assert.False(module.NetworkState().ExposeOnLan);
    }

    [Fact]
    public async Task ARefusedFirewallPromptLeavesTheWarningOnThePage()
    {
        await using var server = FakeOllama.Start("0.35.1");
        var process = new RecordingProcess
        {
            Location = new OllamaLocation(@"C:\Ollama\ollama.exe", null, "0.35.1"),
        };
        process.OnStop = () => server.Version = null;
        process.OnLaunch = () => server.Version = "0.35.1";
        var firewall = new QuietFirewall { Next = FirewallStep.Cancelled };
        var module = new OllamaModule(
            server.Port,
            new OllamaClient(server.Port),
            process,
            TimeSpan.FromSeconds(3),
            firewall,
            _ => { },
            () => null,
            () => ["0.0.0.0"]);

        var result = await module.ApplyListenAsync(true, new CollectingLog(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Other devices may be blocked by Windows Firewall.", result.Failure?.Summary);
        Assert.Contains(result.Failure!.Fixes, fix => fix.Kind == Penelope.Abstractions.FixKind.OpenFirewall);
        Assert.Equal($"0.0.0.0:{server.Port}", process.LastHost);
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Penelope.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Penelope.sln was not found.");
    }

    private static OllamaModule Module(FakeOllama server, RecordingProcess process, OllamaSetup? setup = null)
    {
        return new OllamaModule(
            server.Port,
            new OllamaClient(server.Port),
            process,
            TimeSpan.FromSeconds(3),
            firewall: new QuietFirewall(),
            readHost: () => null,
            listenAddresses: () => ["127.0.0.1"],
            setup: setup);
    }

    private sealed class ScriptedSetup : OllamaSetup
    {
        public string? Latest { get; set; } = "0.36.0";

        public SetupProblem Problem { get; set; } = SetupProblem.None;

        public string? Detail { get; set; }

        public int Installs { get; private set; }

        public Action? BeforeReturn { get; set; }

        public override Task<string?> LatestVersionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Latest);
        }

        public override Task<SetupOutcome> InstallAsync(Penelope.Abstractions.IJobLog log, CancellationToken cancellationToken)
        {
            Installs++;
            log.Progress(1, 2, "Downloading Ollama");
            log.AllowCancel(false);
            BeforeReturn?.Invoke();
            var version = Problem == SetupProblem.None ? "0.36.0" : null;
            return Task.FromResult(new SetupOutcome(Problem, version, Detail));
        }
    }

    private sealed class RecordingProcess : OllamaProcess
    {
        public OllamaLocation Location { get; set; } = new(null, null, null);

        public int Launches { get; private set; }

        public string? LastPath { get; private set; }

        public bool LastServe { get; private set; }

        public string? LastHost { get; private set; }

        public int? Listener { get; set; }

        public List<int> Stopped { get; } = [];

        public Action? OnLaunch { get; set; }

        public Action? OnStop { get; set; }

        public override OllamaLocation Locate() => Location;

        public override int? ListenerPid(int port) => Listener;

        public override int Launch(string path, bool serve)
        {
            Launches++;
            LastPath = path;
            LastServe = serve;
            LastHost = RequestedHost;
            OnLaunch?.Invoke();
            return 7;
        }

        public override void Stop(IReadOnlyCollection<int> processIds, OllamaLocation location)
        {
            Stopped.AddRange(processIds);
            OnStop?.Invoke();
        }
    }

    private sealed class FakeOllama : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cancel = new();

        private FakeOllama(int port)
        {
            Port = port;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        public int Port { get; }

        public string? Version { get; set; }

        public List<string> Unloaded { get; } = [];

        public static FakeOllama Start(string? version)
        {
            var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            var server = new FakeOllama(port) { Version = version };
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
                if (path.EndsWith("/api/version", StringComparison.Ordinal))
                {
                    if (Version is null)
                    {
                        context.Response.StatusCode = 503;
                    }
                    else
                    {
                        await Write(context, "{\"version\":\"" + Version + "\"}");
                        continue;
                    }
                }
                else if (path.EndsWith("/api/ps", StringComparison.Ordinal))
                {
                    var name = Version is null ? "" : "example:latest";
                    var models = name.Length == 0 ? "[]" : "[{" + "\"name\":\"" + name + "\"}]";
                    await Write(context, "{\"models\":" + models + "}");
                    continue;
                }
                else if (path.EndsWith("/api/generate", StringComparison.Ordinal))
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    var body = await reader.ReadToEndAsync();
                    var marker = "\"model\":\"";
                    var index = body.IndexOf(marker, StringComparison.Ordinal);
                    if (index >= 0)
                    {
                        var start = index + marker.Length;
                        var end = body.IndexOf('"', start);
                        if (end > start)
                        {
                            Unloaded.Add(body[start..end]);
                        }
                    }

                    await Write(context, "{}");
                    continue;
                }
                else
                {
                    context.Response.StatusCode = 404;
                }

                context.Response.Close();
            }
        }

        private static async Task Write(HttpListenerContext context, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private sealed class QuietFirewall : LanFirewall
    {
        public bool? LastAllow { get; private set; }

        public FirewallStep Next { get; set; } = FirewallStep.Changed;

        public override bool RuleExists() => false;

        public override FirewallStep Apply(bool allow, string programPath, int port)
        {
            LastAllow = allow;
            return Next;
        }
    }

    private sealed class CollectingLog : Penelope.Abstractions.IJobLog
    {
        public CancellationToken CancellationToken => CancellationToken.None;

        public void Info(string text) { }

        public void Warning(string text) { }

        public void Error(string text) { }

        public void Progress(long completed, long total, string message) { }

        public void AllowCancel(bool allowed) { }
    }
}
