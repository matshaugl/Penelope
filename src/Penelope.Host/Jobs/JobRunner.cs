using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Penelope.Abstractions;

namespace Penelope.Host.Jobs;

public sealed class JobRunner
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(ILogger<JobRunner> logger)
    {
        _logger = logger;
    }

    public JobSnapshot? Find(string id)
    {
        return _jobs.TryGetValue(id, out var job) ? job.Snapshot() : null;
    }

    public string? ActiveJobId(string moduleId)
    {
        var active = _jobs.Values
            .Select(job => job.Snapshot())
            .Where(job => job.ModuleId == moduleId && job.State is "queued" or "running")
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
            .FirstOrDefault(job => job.ModuleId == moduleId && job.Kind == kind && job.State is "queued" or "running");
    }

    public JobSnapshot Start(string moduleId, string kind, Func<IJobLog, CancellationToken, Task<ModuleActionResult>> work)
    {
        var existing = Running(moduleId, kind);
        if (existing is not null)
        {
            return existing;
        }

        var record = new JobRecord(moduleId, kind) { Work = work };
        _jobs[record.Id] = record;
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
        try
        {
            var result = await work(record, record.Token);
            if (record.IsCancellationRequested)
            {
                record.Complete("cancelled", "Cancelled.", null);
            }
            else if (!result.Succeeded)
            {
                record.Complete("failed", result.Failure?.Summary ?? "The step didn't finish.", result.Failure);
            }
            else
            {
                record.Complete("succeeded", "Finished.", null);
            }
        }
        catch (OperationCanceledException)
        {
            record.Complete("cancelled", "Cancelled.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", record.Id);
            record.Error("The operation stopped because of an unexpected error.");
            record.Complete("failed", "Something went wrong.", new FailureCard(
                "Something went wrong.",
                "Penelope couldn't finish this step. Try again.",
                [new FailureFix("retry", "Try again", FixKind.Retry, null)]));
        }
    }
}

public sealed class JobSnapshot
{
    public required string Id { get; init; }

    public required string ModuleId { get; init; }

    public required string Kind { get; init; }

    public required string State { get; init; }

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
    private string _state = "queued";
    private string _message = "Waiting.";
    private long _completed;
    private long _total;
    private bool _cancelAllowed = true;
    private FailureCard? _failure;

    public JobRecord(string moduleId, string kind)
    {
        Id = Guid.NewGuid().ToString("n");
        ModuleId = moduleId;
        Kind = kind;
        StartedAt = DateTimeOffset.UtcNow;
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
            _state = "running";
            _message = "Working.";
        }

        Publish();
    }

    public bool Cancel()
    {
        lock (_gate)
        {
            if (_state is not ("queued" or "running") || !_cancelAllowed)
            {
                return false;
            }
        }

        _cts.Cancel();
        Info("Cancel requested.");
        return true;
    }

    public void Complete(string state, string message, FailureCard? failure)
    {
        lock (_gate)
        {
            if (_state is "succeeded" or "failed" or "cancelled")
            {
                return;
            }

            _state = state;
            _message = message;
            _failure = failure;
            if (_total > 0 && state == "succeeded")
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
            CancelAllowed = _cancelAllowed && _state is "queued" or "running",
            StartedAt = StartedAt,
            Log = _log.ToList(),
            Failure = _failure,
        };
    }

    private sealed record ChannelReaderHolder(Channel<JobSnapshot> Channel);
}
