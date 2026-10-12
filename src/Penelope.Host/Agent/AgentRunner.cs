using Penelope.Abstractions;

namespace Penelope.Host.Agent;

static class AgentRunner
{
    public const int MaxToolCalls = 3;

    public static async IAsyncEnumerable<ChatStreamEvent> RunAsync(
        IModelProvider provider,
        string model,
        IReadOnlyList<ModelMessage> history,
        IReadOnlyList<IAgentTool> tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = new List<ModelMessage> { new("system", ChatPrompts.Agent, null, null) };
        messages.AddRange(history);
        var definitions = tools
            .Select(tool => new ModelToolDefinition(tool.Name, tool.Description, tool.ParametersSchema))
            .ToArray();
        var used = 0;

        for (var round = 0; round < MaxToolCalls + 1; round++)
        {
            var allowTools = used < MaxToolCalls;
            var text = new System.Text.StringBuilder();
            var calls = new List<ModelToolCall>();
            string? failure = null;
            await foreach (var ev in provider.CompleteAsync(
                new ModelCompletion(model, messages, allowTools ? definitions : null),
                cancellationToken))
            {
                if (ev.Kind == ModelEventKind.Token && !string.IsNullOrEmpty(ev.Text))
                {
                    text.Append(ev.Text);
                    yield return ChatStreamEvent.Token(ev.Text);
                }
                else if (ev.Kind == ModelEventKind.ToolCall && !string.IsNullOrWhiteSpace(ev.ToolName))
                {
                    calls.Add(new ModelToolCall(
                        string.IsNullOrWhiteSpace(ev.ToolCallId) ? $"call-{calls.Count + 1}" : ev.ToolCallId!,
                        ev.ToolName!,
                        string.IsNullOrWhiteSpace(ev.ArgumentsJson) ? "{}" : ev.ArgumentsJson!));
                }
                else if (ev.Kind == ModelEventKind.Error)
                {
                    failure = ev.Text ?? "The model didn't answer.";
                    break;
                }
            }

            if (failure is not null)
            {
                yield return ChatStreamEvent.Error(failure);
                yield return ChatStreamEvent.Done();
                yield break;
            }

            if (calls.Count == 0 || !allowTools)
            {
                if (text.Length == 0)
                {
                    yield return ChatStreamEvent.Error(
                        calls.Count == 0 ? "The model returned an empty answer." : "The agent stopped after searching.");
                }

                yield return ChatStreamEvent.Done();
                yield break;
            }

            messages.Add(new ModelMessage("assistant", text.ToString(), calls, null));
            foreach (var call in calls)
            {
                if (used >= MaxToolCalls)
                {
                    break;
                }

                used++;
                var tool = tools.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Name, StringComparison.Ordinal));
                yield return ChatStreamEvent.Status(StatusFor(tool));
                var result = await InvokeAsync(tool, call.ArgumentsJson, cancellationToken);
                messages.Add(new ModelMessage("tool", result, null, call.Name));
            }
        }

        yield return ChatStreamEvent.Error("The agent stopped after searching.");
        yield return ChatStreamEvent.Done();
    }

    private static string StatusFor(IAgentTool? tool)
    {
        return tool?.Name == "web_search" ? "Searching the web…" : "Using a tool…";
    }

    private static async Task<string> InvokeAsync(IAgentTool? tool, string argumentsJson, CancellationToken cancellationToken)
    {
        if (tool is null)
        {
            return "That tool is not available.";
        }

        string result;
        try
        {
            result = await tool.InvokeAsync(argumentsJson, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return "That tool didn't answer.";
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return "The tool returned nothing.";
        }

        return result.Length > 2000 ? result[..2000] : result;
    }
}
