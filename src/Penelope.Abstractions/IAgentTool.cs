namespace Penelope.Abstractions;

public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    string ParametersSchema { get; }

    Task<string> InvokeAsync(string argumentsJson, CancellationToken cancellationToken);
}

public interface IAgentToolSource
{
    IReadOnlyList<IAgentTool> Tools { get; }
}
