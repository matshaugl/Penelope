using Penelope.Abstractions;

namespace Penelope.Host.Plugins;

public sealed class ToolProviderRegistry
{
    private readonly IReadOnlyList<ToolProviderEntry> _providers;

    public ToolProviderRegistry(PluginCatalog catalog)
    {
        _providers = catalog.Plugins
            .Where(plugin => plugin.Module is not null)
            .Select(plugin => new ToolProviderEntry(Resolve(plugin.Module!), plugin.Module!))
            .ToList();
    }

    public IReadOnlyList<ToolProviderEntry> Providers => _providers;

    public IToolModule? FindModule(string id) =>
        _providers.FirstOrDefault(entry => string.Equals(entry.Module.Id, id, StringComparison.OrdinalIgnoreCase))?.Module;

    private static string Resolve(IToolModule module) =>
        module is IToolProvider provider ? provider.ProviderKind : module.Id;
}

public sealed record ToolProviderEntry(string ProviderKind, IToolModule Module);
