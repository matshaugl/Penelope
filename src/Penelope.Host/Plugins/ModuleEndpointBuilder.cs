using Penelope.Abstractions;

namespace Penelope.Host.Plugins;

public sealed class ModuleEndpointBuilder : IModuleEndpointBuilder
{
    private readonly List<Action<IEndpointRouteBuilder>> _maps = [];

    public void MapGet(string pattern, Delegate handler) => _maps.Add(app => app.MapGet(pattern, handler));

    public void MapPost(string pattern, Delegate handler) => _maps.Add(app => app.MapPost(pattern, handler));

    public void MapPut(string pattern, Delegate handler) => _maps.Add(app => app.MapPut(pattern, handler));

    public void MapDelete(string pattern, Delegate handler) => _maps.Add(app => app.MapDelete(pattern, handler));

    public void Apply(IEndpointRouteBuilder group)
    {
        foreach (var map in _maps)
        {
            map(group);
        }
    }
}
