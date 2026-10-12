namespace Penelope.Abstractions;

public interface IJobQueue
{
    string? RunningId(string moduleId, string kind);

    string Start(string moduleId, string kind, Func<IJobLog, CancellationToken, Task<ModuleActionResult>> work);
}

public interface IJobAwareModule
{
    void UseJobs(IJobQueue jobs);
}
