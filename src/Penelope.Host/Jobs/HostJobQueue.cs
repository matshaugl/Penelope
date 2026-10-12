using Penelope.Abstractions;

namespace Penelope.Host.Jobs;

public sealed class HostJobQueue : IJobQueue
{
    private readonly JobRunner _jobs;

    public HostJobQueue(JobRunner jobs)
    {
        _jobs = jobs;
    }

    public string? RunningId(string moduleId, string kind)
    {
        return _jobs.Running(moduleId, kind)?.Id;
    }

    public string Start(string moduleId, string kind, Func<IJobLog, CancellationToken, Task<ModuleActionResult>> work)
    {
        return _jobs.Start(moduleId, kind, work).Id;
    }
}
