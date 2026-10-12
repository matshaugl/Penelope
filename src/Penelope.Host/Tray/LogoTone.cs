using Penelope.Abstractions;
using Penelope.Host.Jobs;
using Penelope.Host.Plugins;

namespace Penelope.Host.Tray;

enum LogoTone
{
    Running,
    Busy,
    Stopped,
}

static class LogoToneMap
{
    public static LogoTone FromModule(ToolAvailability availability, ToolRunState? state, bool jobActive)
    {
        if (availability is ToolAvailability.Error or ToolAvailability.Missing)
        {
            return LogoTone.Stopped;
        }

        if (jobActive || state is ToolRunState.Installing or ToolRunState.Starting or ToolRunState.Stopping)
        {
            return LogoTone.Busy;
        }

        return state == ToolRunState.Running ? LogoTone.Running : LogoTone.Stopped;
    }

    public static LogoTone Combine(IEnumerable<LogoTone> tones)
    {
        var anyRunning = false;
        var any = false;
        foreach (var tone in tones)
        {
            any = true;
            if (tone == LogoTone.Busy)
            {
                return LogoTone.Busy;
            }

            if (tone == LogoTone.Running)
            {
                anyRunning = true;
            }
        }

        if (!any)
        {
            return LogoTone.Stopped;
        }

        return anyRunning ? LogoTone.Running : LogoTone.Stopped;
    }

    public static LogoTone ForCatalog(PluginCatalog catalog, JobRunner jobs)
    {
        return Combine(catalog.Plugins.Select(plugin =>
        {
            if (plugin.Module is null)
            {
                return LogoTone.Stopped;
            }

            var status = plugin.Module.GetStatus();
            return FromModule(plugin.Availability, status.State, jobs.ActiveJobId(plugin.Id) is not null);
        }));
    }
}
