namespace Penelope.Abstractions;

public enum ToolAvailability
{
    Ready,
    Planned,
    Missing,
    Error,
}

public enum ToolRunState
{
    NotInstalled,
    Installing,
    Stopped,
    Starting,
    Running,
    Stopping,
}
