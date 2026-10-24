using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Penelope.Abstractions;

namespace Penelope.Host.Jobs;

public sealed class JobRunner
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();
    private readonly ILogger<JobRunner> _logger;
    private readonly IEventBus _events;
    private readonly IPenelopeDiagnosticLog _diagnostics;

    public JobRunner(ILogger<JobRunner> logger, IEventBus events, IPenelopeDiagnosticLog diagnostics)
    {
        _logger = logger;
        _events = events;
        _diagnostics = diagnostics;
    }

    public JobSnapshot? Find(string id)
    {
        return _jobs.TryGetValue(id, out var job) ? job.Snapshot() : null;
    }

    public string? ActiveJobId(string moduleId)
    {
        var active = _jobs.Values
            .Select(job => job.Snapshot())
            .Where(job => job.ModuleId == moduleId && JobStateNames.IsActive(job.State))
            .OrderByDescending(job => job.StartedAt)
            .FirstOrDefault();
        return active?.Id;
    }

    public string? LatestJobId(string moduleId)
    {
        return _jobs.Values
            .Select(job => job.Snapshot())
            .Where(job => job.ModuleId == moduleId)
            .OrderByDescending(job => job.StartedAt)
            .FirstOrDefault()
            ?.Id;
    }

    public JobSnapshot? Running(string moduleId, string kind)
    {
        return _jobs.Values
            .Select(job => job.Snapshot())
            .FirstOrDefault(job => job.ModuleId == moduleId && job.Kind == kind && JobStateNames.IsActive(job.State));
    }

    public JobSnapshot Start(string moduleId, string kind, Func<IJobLog, CancellationToken, Task<ModuleActionResult>> work)
    {
        var existing = Running(moduleId, kind);
        if (existing is not null)
        {
            return existing;
        }

        var record = new JobRecord(moduleId, kind, _events, _diagnostics) { Work = work };
        _jobs[record.Id] = record;
        PublishJobState(record, JobState.Pending);
        if (kind is "install" or "upgrade")
        {
            _events.Publish(new PenelopeEvent(PenelopeEventNames.InstallStarted, DateTimeOffset.UtcNow, moduleId, record.Id));
        }

        _diagnostics.Write(PenelopeLogLevel.Info, "jobs", $"Started {kind} for {moduleId} ({record.Id}).");
        _ = Task.Run(() => RunAsync(record, work));
        return record.Snapshot();
    }

    public JobSnapshot? Retry(string id)
    {
        if (!_jobs.TryGetValue(id, out var previous) || previous.Work is null)
        {
            return null;
        }

        return Start(previous.ModuleId, previous.Kind, previous.Work);
    }

    public bool Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            return false;
        }

        return job.Cancel();
    }

    public async IAsyncEnumerable<JobSnapshot> Watch(string id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            yield break;
        }

        var reader = job.Subscribe();
        await foreach (var snapshot in reader.ReadAllAsync(cancellationToken))
        {
            yield return snapshot;
        }
    }

    private async Task RunAsync(JobRecord record, Func<IJobLog, CancellationToken, Task<ModuleActionResult>> work)
    {
        record.MarkRunning();
        PublishJobState(record, JobState.Running);
        try
        {
            var result = await work(record, record.Token);
            if (record.IsCancellationRequested)
            {
                record.Complete(JobState.Cancelled, "Cancelled.", null);
                PublishJobState(record, JobState.Cancelled);
            }
            else if (!result.Succeeded)
            {
                record.Complete(JobState.Failed, result.Failure?.Summary ?? "The step didn't finish.", result.Failure);
                PublishJobState(record, JobState.Failed);
                if (record.Kind is "install" or "upgrade")
                {
                    _events.Publish(new PenelopeEvent(
                        PenelopeEventNames.InstallFailed,
                        DateTimeOffset.UtcNow,
                        record.ModuleId,
                        record.Id,
                        Message: result.Failure?.Summary));
                }
            }
            else
            {
                record.Complete(JobState.Completed, "Finished.", null);
                PublishJobState(record, JobState.Completed);
                if (record.Kind is "install" or "upgrade")
                {
                    _events.Publish(new PenelopeEvent(
                        PenelopeEventNames.InstallCompleted,
                        DateTimeOffset.UtcNow,
                        record.ModuleId,
                        record.Id));
                }
            }
        }
        catch (OperationCanceledException)
        {
            record.Complete(JobState.Cancelled, "Cancelled.", null);
            PublishJobState(record, JobState.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", record.Id);
            _diagnostics.Write(PenelopeLogLevel.Error, "jobs", $"Job {record.Id} failed: {ex.Message}");
            record.Error("The operation stopped because of an unexpected error.");
            record.Complete(JobState.Failed, "Something went wrong.", new FailureCard(
                "Something went wrong.",
                "Penelope couldn't finish this step. Try again.",
                [new FailureFix("retry", "Try again", FixKind.Retry, null)]));
            PublishJobState(record, JobState.Failed);
        }
    }

    private void PublishJobState(JobRecord record, JobState state)
    {
        var snapshot = record.Snapshot();
        _events.Publish(new PenelopeEvent(
            PenelopeEventNames.JobStateChanged,
            DateTimeOffset.UtcNow,
            record.ModuleId,
            record.Id,
            snapshot.Completed,
            snapshot.Total,
            JobStateNames.ToApi(state)));
    }
}

public sealed class JobSnapshot
{
    public required string Id { get; init; }

    public required string ModuleId { get; init; }

    public required string Kind { get; init; }

    public required JobState State { get; init; }

    public required string Message { get; init; }

    public long Completed { get; init; }

    public long Total { get; init; }

    public bool CancelAllowed { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public required IReadOnlyList<JobLogLine> Log { get; init; }

    public FailureCard? Failure { get; init; }
}

public sealed record JobLogLine(DateTimeOffset Time, string Level, string Text);

sealed class JobRecord : IJobLog
{
    private readonly object _gate = new();
    private readonly List<JobLogLine> _log = [];
    private readonly List<ChannelReaderHolder> _subscribers = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly IEventBus _events;
    private readonly IPenelopeDiagnosticLog _diagnostics;
    private JobState _state = JobState.Pending;
    private string _message = "Waiting.";
    private long _completed;
    private long _total;
    private bool _cancelAllowed = true;
    private FailureCard? _failure;

    public JobRecord(string moduleId, string kind, IEventBus events, IPenelopeDiagnosticLog diagnostics)
    {
        Id = Guid.NewGuid().ToString("n");
        ModuleId = moduleId;
        Kind = kind;
        StartedAt = DateTimeOffset.UtcNow;
        _events = events;
        _diagnostics = diagnostics;
    }

    public string Id { get; }

    public string ModuleId { get; }

    public string Kind { get; }

    public Func<IJobLog, CancellationToken, Task<ModuleActionResult>>? Work { get; init; }

    public DateTimeOffset StartedAt { get; }

    public CancellationToken CancellationToken => _cts.Token;

    public CancellationToken Token => _cts.Token;

    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    public void Info(string text) => Add("info", text);

    public void Warning(string text) => Add("warning", text);

    public void Error(string text) => Add("error", text);

    public void Progress(long completed, long total, string message)
    {
        lock (_gate)
        {
            _completed = completed;
            _total = total;
            _message = message;
        }

        _events.Publish(new PenelopeEvent(
            PenelopeEventNames.DownloadProgress,
            DateTimeOffset.UtcNow,
            ModuleId,
            Id,
            completed,
            total,
            message));
        Publish();
    }

    public void AllowCancel(bool allowed)
    {
        lock (_gate)
        {
            _cancelAllowed = allowed;
        }

        Publish();
    }

    public void MarkRunning()
    {
        lock (_gate)
        {
            _state = JobState.Running;
            _message = "Working.";
        }

        Publish();
    }

    public bool Cancel()
    {
        lock (_gate)
        {
            if (!JobStateNames.IsActive(_state) || !_cancelAllowed)
            {
                return false;
            }
        }

        _cts.Cancel();
        Info("Cancel requested.");
        return true;
    }

    public void Complete(JobState state, string message, FailureCard? failure)
    {
        lock (_gate)
        {
            if (_state is JobState.Completed or JobState.Failed or JobState.Cancelled)
            {
                return;
            }

            _state = state;
            _message = message;
            _failure = failure;
            if (_total > 0 && state == JobState.Completed)
            {
                _completed = _total;
            }
        }

        Publish();
    }

    public JobSnapshot Snapshot()
    {
        lock (_gate)
        {
            return SnapshotUnlocked();
        }
    }

    public ChannelReader<JobSnapshot> Subscribe()
    {
        var channel = Channel.CreateUnbounded<JobSnapshot>();
        lock (_gate)
        {
            channel.Writer.TryWrite(SnapshotUnlocked());
            _subscribers.Add(new ChannelReaderHolder(channel));
        }

        return channel.Reader;
    }

    private void Add(string level, string text)
    {
        lock (_gate)
        {
            _log.Add(new JobLogLine(DateTimeOffset.UtcNow, level, text));
            if (_log.Count > 500)
            {
                _log.RemoveAt(0);
            }
        }

        var penelopeLevel = level switch
        {
            "warning" => PenelopeLogLevel.Warning,
            "error" => PenelopeLogLevel.Error,
            _ => PenelopeLogLevel.Info,
        };
        _diagnostics.Write(penelopeLevel, $"job:{ModuleId}", text);
        Publish();
    }

    private void Publish()
    {
        JobSnapshot snapshot;
        List<ChannelReaderHolder> subscribers;
        lock (_gate)
        {
            snapshot = SnapshotUnlocked();
            subscribers = _subscribers.ToList();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Channel.Writer.TryWrite(snapshot);
        }
    }

    private JobSnapshot SnapshotUnlocked()
    {
        return new JobSnapshot
        {
            Id = Id,
            ModuleId = ModuleId,
            Kind = Kind,
            State = _state,
            Message = _message,
            Completed = _completed,
            Total = _total,
            CancelAllowed = _cancelAllowed && JobStateNames.IsActive(_state),
            StartedAt = StartedAt,
            Log = _log.ToList(),
            Failure = _failure,
        };
    }

    private sealed record ChannelReaderHolder(Channel<JobSnapshot> Channel);
}
