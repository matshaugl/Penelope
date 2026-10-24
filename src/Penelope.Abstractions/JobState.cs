namespace Penelope.Abstractions;

/// <summary>
/// Canonical job lifecycle: Pending → Running → Completed | Failed | Cancelled. Completed means success only.
/// API values come from <see cref="JobStateNames.ToApi"/>. See docs/JOB_QUEUE.md.
/// </summary>
public enum JobState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public static class JobStateNames
{
    public static string ToApi(JobState state) => state switch
    {
        JobState.Pending => "pending",
        JobState.Running => "running",
        JobState.Completed => "completed",
        JobState.Failed => "failed",
        JobState.Cancelled => "cancelled",
        _ => "pending",
    };

    public static bool IsActive(JobState state) => state is JobState.Pending or JobState.Running;
}
