namespace Penelope.Abstractions;

public interface IModelProvider
{
    string ProviderId { get; }

    Task<ModelAvailability> AvailabilityAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<ModelEvent> CompleteAsync(ModelCompletion request, CancellationToken cancellationToken);
}

public static class ModelEventKind
{
    public const string Token = "token";

    public const string ToolCall = "tool-call";

    public const string Done = "done";

    public const string Error = "error";
}

public sealed record ModelAvailability(
    bool Ready,
    string ProviderName,
    string? Model,
    IReadOnlyList<string> Models,
    string? Reason);

public sealed record ModelCompletion(
    string Model,
    IReadOnlyList<ModelMessage> Messages,
    IReadOnlyList<ModelToolDefinition>? Tools);

public sealed record ModelMessage(
    string Role,
    string Content,
    IReadOnlyList<ModelToolCall>? ToolCalls,
    string? ToolName);

public sealed record ModelToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ModelToolDefinition(string Name, string Description, string ParametersSchema);

public sealed record ModelEvent(string Kind, string? Text, string? ToolName, string? ArgumentsJson, string? ToolCallId);
