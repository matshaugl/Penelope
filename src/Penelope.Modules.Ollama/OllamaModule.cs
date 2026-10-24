using System.ComponentModel;
using Penelope.Abstractions;

namespace Penelope.Modules.Ollama;

public sealed class OllamaModule : IToolProvider, IJobAwareModule, IInfrastructureModule, IModelProvider
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly OllamaClient _client;
    private readonly OllamaApi _api;
    private readonly OllamaProcess _process;
    private readonly LanFirewall _firewall;
    private readonly Action<string> _setHost;
    private readonly Func<string?> _readHost;
    private readonly Func<IReadOnlyList<string>> _listenAddresses;
    private readonly OllamaSetup _setup;
    private IEventBus? _events;
    private IPenelopeDiagnosticLog? _diagnostics;
    private readonly int _port;
    private readonly TimeSpan _startTimeout;
    private readonly List<int> _started = [];
    private OllamaLocation _location = new(null, null, null);
    private ToolRunState _state = ToolRunState.NotInstalled;
    private string? _runningVersion;
    private string? _latest;
    private bool _updateAvailable;
    private DateTimeOffset? _checkedAt;
    private string? _checkError;
    private DateTimeOffset _probedAt = DateTimeOffset.MinValue;

    public OllamaModule()
        : this(OllamaProcess.ReadConfiguredPort(), null, null, TimeSpan.FromSeconds(30), setup: new GitHubOllamaSetup())
    {
    }

    internal OllamaModule(
        int port,
        OllamaClient? client,
        OllamaProcess? process,
        TimeSpan startTimeout,
        LanFirewall? firewall = null,
        Action<string>? setHost = null,
        Func<string?>? readHost = null,
        Func<IReadOnlyList<string>>? listenAddresses = null,
        OllamaSetup? setup = null)
    {
        _port = port;
        _client = client ?? new OllamaClient(port);
        _api = new OllamaApi(_client, this);
        _process = process ?? new OllamaProcess();
        _firewall = firewall ?? new LanFirewall();
        _setHost = setHost ?? RememberHost;
        _readHost = readHost ?? ReadSavedHost;
        _listenAddresses = listenAddresses ?? (() => TcpListeners.ListenAddresses(_port));
        _setup = setup ?? new OfflineOllamaSetup();
        _startTimeout = startTimeout;
    }

    public string Id => "ollama";

    public string ProviderKind => ToolProviderKinds.Ollama;

    public string ProviderId => "ollama";

    public string DisplayName => "Ollama";

    public string Summary => "The local model server. Penelope starts and stops it on this computer.";

    public ToolAvailability Availability => ToolAvailability.Ready;

    public IReadOnlyList<ModuleNavItem> Nav =>
    [
        new ModuleNavItem("Models", "/ollama/models"),
        new ModuleNavItem("Add a model", "/ollama/catalog"),
        new ModuleNavItem("Network", "/ollama/network"),
    ];

    public ToolStatus GetStatus()
    {
        ProbeIfStale();
        lock (_gate)
        {
            return new ToolStatus(_state, Headline(_state));
        }
    }

    public ToolRelease GetRelease()
    {
        ProbeIfStale();
        lock (_gate)
        {
            var installed = _state is ToolRunState.NotInstalled ? null : _runningVersion ?? _location.Version;
            return new ToolRelease(installed, _latest, _checkedAt, _updateAvailable, _checkError);
        }
    }

    public async Task CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        _probedAt = DateTimeOffset.MinValue;
        ProbeIfStale();
        try
        {
            var latest = await _setup.LatestVersionAsync(cancellationToken);
            RememberLatest(latest, InstalledVersion());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsLookupFailure(ex))
        {
            lock (_gate)
            {
                _checkError = "Penelope couldn't reach the Ollama release.";
                _updateAvailable = false;
                _checkedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public InstallPlan PlanInstall()
    {
        ProbeIfStale();
        var present = Current().IsPresent;
        if (present)
        {
            return new InstallPlan([new InstallStep(
                "Already here",
                "Ollama is already on this computer. Penelope will not download another copy.")]);
        }

        return new InstallPlan(
        [
            new InstallStep("Download", "Penelope downloads the official Windows installer from the Ollama release."),
            new InstallStep("Check", "Penelope checks the published checksum before the installer runs."),
            new InstallStep("Install", "The installer stays on this user account. Windows may ask for permission. Penelope will not ask you to type a command."),
        ]);
    }

    public async Task<ModuleActionResult> InstallAsync(IJobLog log, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            return await InstallCore(upgrade: false, log, cancellationToken);
        }
        finally
        {
            _operation.Release();
            _probedAt = DateTimeOffset.UtcNow;
        }
    }

    public async Task<ModuleActionResult> UpgradeAsync(IJobLog log, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            return await InstallCore(upgrade: true, log, cancellationToken);
        }
        finally
        {
            _operation.Release();
            _probedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<ModuleActionResult> InstallCore(bool upgrade, IJobLog log, CancellationToken cancellationToken)
    {
        ProbeIfStale();
        var present = Current().IsPresent || _runningVersion is not null;
        if (present && !upgrade)
        {
            log.Info("Ollama is already on this computer.");
            return new ModuleActionResult(true, null);
        }

        if (upgrade)
        {
            if (!present)
            {
                log.Info("Ollama is not installed yet. Penelope will install it.");
            }
            else
            {
                string? latest;
                try
                {
                    latest = _latest ?? await _setup.LatestVersionAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsLookupFailure(ex))
                {
                    return ReleaseUnreachable();
                }

                var installed = InstalledVersion();
                if (latest is null)
                {
                    return ReleaseUnreachable();
                }

                RememberLatest(latest, installed);
                if (installed is not null && !OllamaRelease.IsNewer(latest, installed))
                {
                    log.Info("Ollama is already the latest version.");
                    return new ModuleActionResult(true, null);
                }

                if (await _client.GetVersionAsync(cancellationToken) is not null)
                {
                    log.Info("Ollama will stop so the installer can replace it.");
                    var stopped = await StopCore(cancellationToken);
                    if (!stopped.Succeeded)
                    {
                        return LockedCard();
                    }
                }
            }
        }

        SetState(ToolRunState.Installing);
        log.AllowCancel(true);
        log.Info("Windows may ask for permission. The installer stays on this user account.");
        SetupOutcome outcome;
        try
        {
            outcome = await _setup.InstallAsync(log, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var locationNow = _process.Locate();
            RememberLocation(locationNow);
            SetState(locationNow.IsPresent ? ToolRunState.Stopped : ToolRunState.NotInstalled);
            throw;
        }

        if (outcome.Problem != SetupProblem.None)
        {
            var failedLocation = _process.Locate();
            RememberLocation(failedLocation);
            SetState(failedLocation.IsPresent ? ToolRunState.Stopped : ToolRunState.NotInstalled);
            return FailureFor(outcome);
        }

        var found = _process.Locate();
        RememberLocation(found);
        if (!found.IsPresent)
        {
            SetState(ToolRunState.NotInstalled);
            return new ModuleActionResult(false, new FailureCard(
                "Ollama didn't install.",
                "The installer finished, and Penelope still can't find Ollama.",
                [new FailureFix("check-again", "Look again", FixKind.CheckAgain, null), DownloadFix()]));
        }

        if (outcome.Version is not null)
        {
            RememberLatest(outcome.Version, found.Version ?? outcome.Version);
        }

        var live = await _client.GetVersionAsync(cancellationToken);
        if (live is not null)
        {
            RememberRunning(live);
        }
        else
        {
            SetState(ToolRunState.Stopped);
        }

        log.Info("Ollama is on this computer.");
        return new ModuleActionResult(true, null);
    }

    public async Task<ModuleActionResult> StartAsync(CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            var version = await _client.GetVersionAsync(cancellationToken);
            if (version is not null)
            {
                RememberRunning(version);
                PublishToolEvent(PenelopeEventNames.ToolStarted);
                return new ModuleActionResult(true, null);
            }

            var location = _process.Locate();
            RememberLocation(location);
            if (!location.IsPresent)
            {
                SetState(ToolRunState.NotInstalled);
                return NotInstalledResult();
            }

            if (location.ExecutablePath is null)
            {
                SetState(ToolRunState.Stopped);
                return new ModuleActionResult(false, new FailureCard(
                    "Ollama is listed, and the program is missing.",
                    "Windows says Ollama was installed, and Penelope can't find it.",
                    [DownloadFix()]));
            }

            SetState(ToolRunState.Starting);
            var pid = LaunchForCurrentMode(location);
            lock (_gate)
            {
                _started.Add(pid);
            }

            var deadline = DateTime.UtcNow + _startTimeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                version = await _client.GetVersionAsync(cancellationToken);
                if (version is not null)
                {
                    RememberRunning(version);
                    PublishToolEvent(PenelopeEventNames.ToolStarted);
                    return new ModuleActionResult(true, null);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            }

            SetState(ToolRunState.Stopped);
            return new ModuleActionResult(false, new FailureCard(
                "Ollama didn't start.",
                "Penelope opened Ollama and waited, and it never answered on this computer.",
                []));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            SetState(ToolRunState.Stopped);
            return new ModuleActionResult(false, new FailureCard(
                "Ollama didn't start.",
                "Penelope couldn't open Ollama on this computer.",
                []));
        }
        catch (OperationCanceledException)
        {
            SetState(ToolRunState.Stopped);
            throw;
        }
        finally
        {
            _operation.Release();
            _probedAt = DateTimeOffset.UtcNow;
        }
    }

    public async Task<ModuleActionResult> ApplyListenAsync(bool exposeOnLan, IJobLog log, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            log.AllowCancel(false);
            var location = _process.Locate();
            RememberLocation(location);
            if (location.ExecutablePath is null)
            {
                return location.IsPresent
                    ? new ModuleActionResult(false, new FailureCard(
                        "Ollama is listed, and the program is missing.",
                        "Windows says Ollama was installed, and Penelope can't find it.",
                        [DownloadFix()]))
                    : NotInstalledResult();
            }

            var host = OllamaListen.HostValue(exposeOnLan, _port);
            log.Info(exposeOnLan
                ? "Ollama will restart so other devices on this network can reach it."
                : "Ollama will restart so only this computer can reach it.");
            log.Info(OllamaListen.Warning);

            var stopped = await StopCore(cancellationToken);
            if (!stopped.Succeeded)
            {
                return stopped;
            }

            _setHost(host);
            SetState(ToolRunState.Starting);
            var pid = _process.LaunchServing(location.ExecutablePath, host);
            lock (_gate)
            {
                _started.Add(pid);
            }

            log.Info("Waiting for Ollama to answer.");
            var version = await WaitForVersion(cancellationToken);
            if (version is null)
            {
                SetState(ToolRunState.Stopped);
                return new ModuleActionResult(false, new FailureCard(
                    "Ollama didn't start.",
                    "Penelope restarted Ollama and it never answered on this computer.",
                    []));
            }

            RememberRunning(version);
            var firewall = _firewall.Apply(exposeOnLan, location.ExecutablePath, _port);
            if (exposeOnLan && firewall == FirewallStep.Cancelled)
            {
                log.Warning("Windows Firewall was not changed.");
                return new ModuleActionResult(false, new FailureCard(
                    "Other devices may be blocked by Windows Firewall.",
                    "Ollama is listening for other devices, and the firewall rule was not added. Penelope did not change its own page.",
                    [
                        new FailureFix("retry", "Try again", FixKind.Retry, null),
                        new FailureFix("open-firewall", "Open Windows Firewall", FixKind.OpenFirewall, null),
                    ]));
            }

            if (exposeOnLan && firewall == FirewallStep.Failed)
            {
                log.Warning("The firewall rule was not added.");
                return new ModuleActionResult(false, new FailureCard(
                    "Other devices may be blocked by Windows Firewall.",
                    "Ollama is listening for other devices. The firewall rule could not be added.",
                    [
                        new FailureFix("retry", "Try again", FixKind.Retry, null),
                        new FailureFix("open-firewall", "Open Windows Firewall", FixKind.OpenFirewall, null),
                    ]));
            }

            if (!exposeOnLan && firewall == FirewallStep.Cancelled)
            {
                log.Warning("The old firewall rule may still be there. Ollama is listening only on this computer.");
            }

            log.Info(exposeOnLan ? "Other devices can use the address on this page." : "Only this computer can reach Ollama.");
            return new ModuleActionResult(true, null);
        }
        finally
        {
            _operation.Release();
            _probedAt = DateTimeOffset.UtcNow;
        }
    }

    public OllamaNetworkState NetworkState()
    {
        var listen = _listenAddresses();
        var exposed = OllamaListen.IsExposed(listen);
        var running = false;
        lock (_gate)
        {
            running = _state == ToolRunState.Running;
        }

        return new OllamaNetworkState(
            exposed,
            _port,
            running,
            listen,
            exposed ? OllamaListen.ShareableUrls(_port) : [],
            _firewall.RuleExists(),
            OllamaListen.Warning);
    }

    public async Task<ModuleActionResult> StopAsync(CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            return await StopCore(cancellationToken);
        }
        finally
        {
            _operation.Release();
            _probedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<ModuleActionResult> StopCore(CancellationToken cancellationToken)
    {
            var version = await _client.GetVersionAsync(cancellationToken);
            if (version is null)
            {
                var location = _process.Locate();
                RememberLocation(location);
                SetState(location.IsPresent ? ToolRunState.Stopped : ToolRunState.NotInstalled);
                PublishToolEvent(PenelopeEventNames.ToolStopped);
                return new ModuleActionResult(true, null);
            }

            SetState(ToolRunState.Stopping);
            await _client.UnloadLoadedModelsAsync(cancellationToken);
            var locationNow = _process.Locate();
            int[] started;
            lock (_gate)
            {
                started = _started.ToArray();
                _started.Clear();
            }

            var pids = new List<int>();
            var listener = _process.ListenerPid(_port);
            if (listener is int listenerId)
            {
                pids.Add(listenerId);
            }

            pids.AddRange(started);
            _process.Stop(pids, locationNow);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await _client.GetVersionAsync(cancellationToken) is null)
                {
                    RememberLocation(locationNow);
                    SetState(locationNow.IsPresent ? ToolRunState.Stopped : ToolRunState.NotInstalled);
                    PublishToolEvent(PenelopeEventNames.ToolStopped);
                    return new ModuleActionResult(true, null);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            }

            RememberRunning(version);
            return new ModuleActionResult(false, new FailureCard(
                "Ollama is still running.",
                "Penelope asked Ollama to stop, and it is still answering.",
                []));
    }

    public void UseInfrastructure(HostInfrastructure infrastructure)
    {
        if (_setup is GitHubOllamaSetup github)
        {
            github.Attach(infrastructure);
        }

        _events = infrastructure.Events;
        _diagnostics = infrastructure.Diagnostics;
        _client.OnModelPulled = name => _events?.Publish(new PenelopeEvent(
            PenelopeEventNames.ModelAdded,
            DateTimeOffset.UtcNow,
            Id,
            Message: name));
    }

    public void UseJobs(IJobQueue jobs)
    {
        _api.UseJobs(jobs);
    }

    private void PublishToolEvent(string name)
    {
        _events?.Publish(new PenelopeEvent(name, DateTimeOffset.UtcNow, Id));
        _diagnostics?.Write(PenelopeLogLevel.Info, ProviderKind, name);
    }

    public void ContributeApi(IModuleEndpointBuilder endpoints)
    {
        _api.Map(endpoints);
    }

    private void ProbeIfStale()
    {
        if (DateTimeOffset.UtcNow - _probedAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        if (_state is ToolRunState.Starting or ToolRunState.Stopping)
        {
            return;
        }

        var location = _process.Locate();
        var version = _client.GetVersionAsync(CancellationToken.None).GetAwaiter().GetResult();
        lock (_gate)
        {
            _location = location;
            _probedAt = DateTimeOffset.UtcNow;
            if (version is not null)
            {
                _runningVersion = version;
                _state = ToolRunState.Running;
            }
            else if (location.IsPresent)
            {
                _runningVersion = null;
                _state = ToolRunState.Stopped;
            }
            else
            {
                _runningVersion = null;
                _state = ToolRunState.NotInstalled;
            }
        }
    }

    private OllamaLocation Current()
    {
        lock (_gate)
        {
            return _location;
        }
    }

    private void RememberRunning(string version)
    {
        lock (_gate)
        {
            _runningVersion = version;
            _state = ToolRunState.Running;
            _probedAt = DateTimeOffset.UtcNow;
        }
    }

    private void RememberLocation(OllamaLocation location)
    {
        lock (_gate)
        {
            _location = location;
            if (location.Version is not null && _runningVersion is null)
            {
                _runningVersion = null;
            }
        }
    }

    private void SetState(ToolRunState state)
    {
        lock (_gate)
        {
            _state = state;
            if (state is not ToolRunState.Running)
            {
                _runningVersion = state is ToolRunState.Stopped ? _location.Version : _runningVersion;
            }
        }
    }

    private static string Headline(ToolRunState state)
    {
        return state switch
        {
            ToolRunState.Running => "Ollama is running",
            ToolRunState.Stopped => "Ollama is stopped",
            ToolRunState.Installing => "Ollama is installing",
            ToolRunState.Starting => "Ollama is starting",
            ToolRunState.Stopping => "Ollama is stopping",
            _ => "Ollama is not installed",
        };
    }

    private static ModuleActionResult NotInstalledResult()
    {
        return new ModuleActionResult(false, new FailureCard(
            "Ollama is not installed.",
            "Use Install Ollama on this page. Penelope downloads the official installer.",
            [DownloadFix()]));
    }

    private static FailureFix DownloadFix()
    {
        return new FailureFix("download", "Open the Ollama download page", FixKind.OpenUrl, OllamaRelease.DownloadPage);
    }

    private int LaunchForCurrentMode(OllamaLocation location)
    {
        var saved = _readHost();
        if (saved is not null && saved.StartsWith("0.0.0.0:", StringComparison.Ordinal) && location.ExecutablePath is not null)
        {
            return _process.LaunchServing(location.ExecutablePath, saved);
        }

        return location.AppPath is not null
            ? _process.Launch(location.AppPath, serve: false)
            : _process.Launch(location.ExecutablePath!, serve: true);
    }

    private async Task<string?> WaitForVersion(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _startTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var version = await _client.GetVersionAsync(cancellationToken);
            if (version is not null)
            {
                return version;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }

        return null;
    }

    private string? InstalledVersion()
    {
        lock (_gate)
        {
            return _state is ToolRunState.NotInstalled ? null : _runningVersion ?? _location.Version;
        }
    }

    private void RememberLatest(string? latest, string? installed)
    {
        lock (_gate)
        {
            _checkedAt = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(latest))
            {
                _latest = null;
                _updateAvailable = false;
                _checkError = "Penelope couldn't find the Ollama release.";
                return;
            }

            _latest = OllamaRelease.Normalize(latest);
            _checkError = null;
            _updateAvailable = installed is not null && OllamaRelease.IsNewer(_latest, installed);
        }
    }

    private static bool IsLookupFailure(Exception ex)
    {
        return ex is HttpRequestException or IOException or InvalidOperationException or System.Text.Json.JsonException or TaskCanceledException;
    }

    private static ModuleActionResult ReleaseUnreachable()
    {
        return new ModuleActionResult(false, new FailureCard(
            "Ollama didn't update.",
            "Penelope couldn't reach the Ollama release.",
            [new FailureFix("retry", "Try again", FixKind.Retry, null)]));
    }

    private static ModuleActionResult LockedCard()
    {
        return new ModuleActionResult(false, new FailureCard(
            "Ollama didn't install.",
            "Ollama is open and the installer couldn't replace it.",
            [new FailureFix("retry", "Stop Ollama and try again", FixKind.Retry, null)]));
    }

    private static ModuleActionResult FailureFor(SetupOutcome outcome)
    {
        return outcome.Problem switch
        {
            SetupProblem.Checksum => new ModuleActionResult(false, new FailureCard(
                "Ollama didn't install.",
                outcome.Detail ?? "The downloaded installer didn't match the published checksum.",
                [new FailureFix("retry", "Try again", FixKind.Retry, null)])),
            SetupProblem.Disk => new ModuleActionResult(false, new FailureCard(
                "Ollama didn't install.",
                "The models disk does not have room for this download.",
                [new FailureFix("open-disk", "Open the drive", FixKind.OpenDisk, null)])),
            SetupProblem.Cancelled => new ModuleActionResult(false, new FailureCard(
                "Ollama didn't install.",
                "Windows asked for permission and the request was cancelled.",
                [new FailureFix("retry", "Try again", FixKind.Retry, null), DownloadFix()])),
            SetupProblem.Locked => LockedCard(),
            SetupProblem.Failed => new ModuleActionResult(false, new FailureCard(
                "Ollama didn't install.",
                "The Ollama installer stopped before it finished.",
                [new FailureFix("retry", "Try again", FixKind.Retry, null), DownloadFix()])),
            _ => new ModuleActionResult(false, new FailureCard(
                "Ollama didn't install.",
                outcome.Detail ?? "Penelope couldn't reach the Ollama release.",
                [new FailureFix("retry", "Try again", FixKind.Retry, null)])),
        };
    }

    private static string? ReadSavedHost()
    {
        var saved = Environment.GetEnvironmentVariable("OLLAMA_HOST", EnvironmentVariableTarget.User);
        return string.IsNullOrWhiteSpace(saved) ? null : saved;
    }

    private void RememberHost(string host)
    {
        Environment.SetEnvironmentVariable("OLLAMA_HOST", host, EnvironmentVariableTarget.User);
        PatchExposeFlag(host.StartsWith("0.0.0.0:", StringComparison.Ordinal));
    }

    private static void PatchExposeFlag(bool expose)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Penelope",
                "config.json");
            System.Text.Json.Nodes.JsonObject root;
            if (File.Exists(path))
            {
                root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) as System.Text.Json.Nodes.JsonObject
                    ?? new System.Text.Json.Nodes.JsonObject();
            }
            else
            {
                root = new System.Text.Json.Nodes.JsonObject();
            }

            root["exposeOllamaOnLan"] = expose;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }
    }

    public async Task<ModelAvailability> AvailabilityAsync(CancellationToken cancellationToken)
    {
        var status = GetStatus();
        if (status.State != ToolRunState.Running)
        {
            return new ModelAvailability(false, "Ollama", null, [], status.Headline + ".");
        }

        try
        {
            var models = await _client.ListModelsAsync(cancellationToken);
            var names = models.Select(model => model.Name).ToArray();
            if (names.Length == 0)
            {
                return new ModelAvailability(false, "Ollama", null, [], "Add a model before trying one.");
            }

            var active = models.FirstOrDefault(model => model.InMemory)?.Name ?? names[0];
            return new ModelAvailability(true, "Ollama", active, names, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            return new ModelAvailability(false, "Ollama", null, [], "Ollama didn't list its models.");
        }
    }

    public IAsyncEnumerable<ModelEvent> CompleteAsync(ModelCompletion request, CancellationToken cancellationToken)
    {
        return _client.ChatAsync(request, cancellationToken);
    }
}

public sealed record OllamaNetworkState(
    bool ExposeOnLan,
    int Port,
    bool Running,
    IReadOnlyList<string> Listen,
    IReadOnlyList<string> Shareable,
    bool FirewallRule,
    string Warning);
