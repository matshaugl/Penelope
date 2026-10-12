namespace Penelope.Abstractions;

public interface IJobLog
{
    CancellationToken CancellationToken { get; }

    void Info(string text);

    void Warning(string text);

    void Error(string text);

    void Progress(long completed, long total, string message);

    void AllowCancel(bool allowed);
}
