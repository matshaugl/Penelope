namespace Penelope.Abstractions;

public enum PenelopeLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public interface IPenelopeDiagnosticLog
{
    void Write(PenelopeLogLevel level, string category, string message);

    IReadOnlyList<PenelopeLogEntry> Recent(int limit = 200);
}

public sealed record PenelopeLogEntry(DateTimeOffset Time, PenelopeLogLevel Level, string Category, string Message);
