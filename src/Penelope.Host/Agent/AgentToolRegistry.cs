using Penelope.Abstractions;
using Penelope.Host.Plugins;

namespace Penelope.Host.Agent;

sealed class AgentToolRegistry
{
    public AgentToolRegistry(IEnumerable<IAgentToolSource> sources, PluginCatalog catalog)
    {
        var all = sources.ToList();
        foreach (var plugin in catalog.Plugins)
        {
            if (plugin.Module is IAgentToolSource source)
            {
                all.Add(source);
            }
        }

        Tools = Collect(all);
    }

    public IReadOnlyList<IAgentTool> Tools { get; }

    public static IReadOnlyList<IAgentTool> Collect(IEnumerable<IAgentToolSource> sources)
    {
        var tools = new List<IAgentTool>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            foreach (var tool in source.Tools)
            {
                if (names.Add(tool.Name))
                {
                    tools.Add(tool);
                }
            }
        }

        return tools;
    }
}
