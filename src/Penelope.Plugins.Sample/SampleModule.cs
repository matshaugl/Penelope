using Penelope.Abstractions;

namespace Penelope.Plugins.Sample;

public sealed class SampleModule : IToolModule
{
    private readonly object _gate = new();
    private int _installAttempts;
    private bool _installed;
    private string _version = "0.1.0";
    private readonly string _latest = "0.2.0";
    private DateTimeOffset _checkedAt = DateTimeOffset.UtcNow;
    private ToolRunState _state = ToolRunState.NotInstalled;

    public string Id => "sample";

    public string DisplayName => "Sample tool";

    public string Summary => "A placeholder that proves a new tool can appear without changing Penelope. It does not install anything on this computer.";

    public ToolAvailability Availability => ToolAvailability.Ready;

    public IReadOnlyList<ModuleNavItem> Nav => [];

    public ToolStatus GetStatus()
    {
        lock (_gate)
        {
            var headline = _state switch
            {
                ToolRunState.Running => "Sample tool is running",
                ToolRunState.Stopped => "Sample tool is stopped",
                ToolRunState.Installing => "Sample tool is installing",
                ToolRunState.Starting => "Sample tool is starting",
                ToolRunState.Stopping => "Sample tool is stopping",
                _ => "Sample tool is not installed",
            };
            return new ToolStatus(_state, headline);
        }
    }

    public ToolRelease GetRelease()
    {
        lock (_gate)
        {
            return new ToolRelease(
                _installed ? _version : null,
                _latest,
                _checkedAt,
                _installed && _version != _latest,
                null);
        }
    }

    public Task CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _checkedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    public InstallPlan PlanInstall()
    {
        return new InstallPlan(
        [
            new InstallStep("Prepare", "Penelope will run a sample install and write a log on this page."),
            new InstallStep("Finish", "Nothing is copied onto the computer. The first try stops so you can see a failure."),
        ]);
    }

    public async Task<ModuleActionResult> InstallAsync(IJobLog log, CancellationToken cancellationToken)
    {
        SetState(ToolRunState.Installing);
        try
        {
            log.Progress(0, 3, "Preparing the sample install");
            log.Info("This is a sample plugin. Nothing is installed on the computer.");
            await Task.Delay(200, cancellationToken);
            log.Progress(1, 3, "Checking the sample");
            log.Info("Looking at the sample package.");
            await Task.Delay(200, cancellationToken);

            var attempt = NextAttempt();
            if (attempt == 1)
            {
                log.Progress(2, 3, "The sample installer stopped");
                log.Error("The sample installer stopped before it finished.");
                SetState(ToolRunState.NotInstalled);
                return new ModuleActionResult(false, new FailureCard(
                    "Sample tool didn't install.",
                    "This sample stops once so the page can show a failure and a fix. Try again to finish.",
                    [new FailureFix("retry", "Try again", FixKind.Retry, null)]));
            }

            lock (_gate)
            {
                _installed = true;
                _version = "0.1.0";
                _state = ToolRunState.Stopped;
            }

            log.Progress(3, 3, "Sample tool is installed");
            log.Info("Sample tool is ready.");
            return new ModuleActionResult(true, null);
        }
        catch (OperationCanceledException)
        {
            SetState(_installedSnapshot() ? ToolRunState.Stopped : ToolRunState.NotInstalled);
            throw;
        }
    }

    public async Task<ModuleActionResult> UpgradeAsync(IJobLog log, CancellationToken cancellationToken)
    {
        if (!IsInstalled())
        {
            log.Error("Sample tool is not installed yet.");
            return new ModuleActionResult(false, new FailureCard(
                "Sample tool is not installed.",
                "Install it first. The update can run after that.",
                []));
        }

        log.Progress(0, 2, "Preparing the sample update");
        log.Info("Updating the sample from 0.1.0 to 0.2.0.");
        await Task.Delay(200, cancellationToken);
        lock (_gate)
        {
            _version = _latest;
            _checkedAt = DateTimeOffset.UtcNow;
        }

        log.Progress(2, 2, "Sample tool is up to date");
        log.Info("Sample tool is now 0.2.0.");
        return new ModuleActionResult(true, null);
    }

    public Task<ModuleActionResult> StartAsync(CancellationToken cancellationToken)
    {
        if (!IsInstalled())
        {
            return Task.FromResult(new ModuleActionResult(false, new FailureCard(
                "Sample tool is not installed.",
                "Install it before starting it.",
                [new FailureFix("retry", "Try again", FixKind.Retry, null)])));
        }

        lock (_gate)
        {
            _state = ToolRunState.Running;
        }

        return Task.FromResult(new ModuleActionResult(true, null));
    }

    public Task<ModuleActionResult> StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _state = _installed ? ToolRunState.Stopped : ToolRunState.NotInstalled;
        }

        return Task.FromResult(new ModuleActionResult(true, null));
    }

    public void ContributeApi(IModuleEndpointBuilder endpoints)
    {
    }

    private int NextAttempt()
    {
        lock (_gate)
        {
            _installAttempts++;
            return _installAttempts;
        }
    }

    private bool IsInstalled()
    {
        lock (_gate)
        {
            return _installed;
        }
    }

    private bool _installedSnapshot()
    {
        lock (_gate)
        {
            return _installed;
        }
    }

    private void SetState(ToolRunState state)
    {
        lock (_gate)
        {
            _state = state;
        }
    }
}
