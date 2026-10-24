namespace Penelope.Abstractions;

public interface IEventBus
{
    void Publish(PenelopeEvent evt);

    IDisposable Subscribe(string eventName, Action<PenelopeEvent> handler);

    IReadOnlyList<PenelopeEvent> Recent(int limit = 100);
}

public sealed record PenelopeEvent(
    string Name,
    DateTimeOffset Time,
    string? ToolId = null,
    string? JobId = null,
    long? Completed = null,
    long? Total = null,
    string? Message = null);

/// <summary>
/// Canonical event names, <c>{category}.{past-tense-verb}</c>. The naming standard and which names are
/// published today versus reserved are in docs/EVENT_BUS.md. Publish only names defined here.
/// </summary>
public static class PenelopeEventNames
{
    public const string ToolStarted = "tool.started";

    public const string ToolStopped = "tool.stopped";

    /// <summary>Reserved. No publisher yet.</summary>
    public const string ToolError = "tool.error";

    /// <summary>Reserved. No publisher yet; <see cref="JobStateChanged"/> carries these transitions today.</summary>
    public const string JobCreated = "job.created";

    /// <summary>Reserved. No publisher yet; <see cref="JobStateChanged"/> carries these transitions today.</summary>
    public const string JobStarted = "job.started";

    /// <summary>Reserved. No publisher yet; <see cref="JobStateChanged"/> carries these transitions today.</summary>
    public const string JobCompleted = "job.completed";

    /// <summary>Reserved. No publisher yet; <see cref="JobStateChanged"/> carries these transitions today.</summary>
    public const string JobFailed = "job.failed";

    /// <summary>Reserved. No publisher yet; <see cref="JobStateChanged"/> carries these transitions today.</summary>
    public const string JobCancelled = "job.cancelled";

    /// <summary>
    /// Transitional aggregate published on every job transition; <see cref="PenelopeEvent.Message"/> is the
    /// API state value. Kept until the per-state <c>job.*</c> events are published.
    /// </summary>
    public const string JobStateChanged = "job.state-changed";

    public const string InstallStarted = "install.started";

    public const string InstallCompleted = "install.completed";

    public const string InstallFailed = "install.failed";

    /// <summary>Reserved. No publisher yet.</summary>
    public const string DownloadStarted = "download.started";

    public const string DownloadProgress = "download.progress";

    /// <summary>Reserved. No publisher yet.</summary>
    public const string DownloadCompleted = "download.completed";

    /// <summary>Reserved. No publisher yet.</summary>
    public const string DownloadFailed = "download.failed";

    public const string ModelAdded = "model.added";

    /// <summary>Reserved. No publisher yet.</summary>
    public const string ModelRemoved = "model.removed";

    /// <summary>Reserved. No publisher yet.</summary>
    public const string ModelLoaded = "model.loaded";

    /// <summary>Reserved. No publisher yet.</summary>
    public const string ModelUnloaded = "model.unloaded";
}
