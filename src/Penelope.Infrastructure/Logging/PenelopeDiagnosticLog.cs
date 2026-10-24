using Penelope.Abstractions;

namespace Penelope.Infrastructure.Logging;

public sealed class PenelopeDiagnosticLog : IPenelopeDiagnosticLog
{
    private readonly object _gate = new();
    private readonly List<PenelopeLogEntry> _recent = [];
    private readonly string _logPath;

    public PenelopeDiagnosticLog(string? logDirectory = null)
    {
        var directory = logDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Penelope", "logs");
        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, "penelope.log");
    }

    public void Write(PenelopeLogLevel level, string category, string message)
    {
        var entry = new PenelopeLogEntry(DateTimeOffset.UtcNow, level, category, message);
        lock (_gate)
        {
            _recent.Add(entry);
            if (_recent.Count > 1000)
            {
                _recent.RemoveAt(0);
            }
        }

        var line = $"{entry.Time:O} [{level.ToString().ToUpperInvariant()}] {category}: {message}{Environment.NewLine}";
        try
        {
            File.AppendAllText(_logPath, line);
        }
        catch
        {
            // Logging must not break the host.
        }
    }

    public IReadOnlyList<PenelopeLogEntry> Recent(int limit = 200)
    {
        lock (_gate)
        {
            return _recent.TakeLast(limit).ToList();
        }
    }

    public string LogPath => _logPath;
}
