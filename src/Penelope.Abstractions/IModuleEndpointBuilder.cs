namespace Penelope.Abstractions;

public interface IModuleEndpointBuilder
{
    void MapGet(string pattern, Delegate handler);

    void MapPost(string pattern, Delegate handler);

    void MapPut(string pattern, Delegate handler);

    void MapDelete(string pattern, Delegate handler);
}
