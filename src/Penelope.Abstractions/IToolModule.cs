namespace Penelope.Abstractions;

public interface IToolModule
{
    string Id { get; }

    string DisplayName { get; }

    string Summary { get; }

    ToolAvailability Availability { get; }

    IReadOnlyList<ModuleNavItem> Nav { get; }

    ToolStatus GetStatus();

    ToolRelease GetRelease();

    Task CheckForUpdateAsync(CancellationToken cancellationToken);

    InstallPlan PlanInstall();

    Task<ModuleActionResult> InstallAsync(IJobLog log, CancellationToken cancellationToken);

    Task<ModuleActionResult> UpgradeAsync(IJobLog log, CancellationToken cancellationToken);

    Task<ModuleActionResult> StartAsync(CancellationToken cancellationToken);

    Task<ModuleActionResult> StopAsync(CancellationToken cancellationToken);

    void ContributeApi(IModuleEndpointBuilder endpoints);
}
