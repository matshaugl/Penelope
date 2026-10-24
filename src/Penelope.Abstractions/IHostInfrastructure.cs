namespace Penelope.Abstractions;

/// <summary>
/// Host services injected into plugins after load. Keeps plugins free of host project references.
/// </summary>
public sealed record HostInfrastructure(
    IDownloadManager Downloads,
    IEventBus Events,
    IPenelopeDiagnosticLog Diagnostics,
    IJobQueue Jobs);

public interface IInfrastructureModule
{
    void UseInfrastructure(HostInfrastructure infrastructure);
}
