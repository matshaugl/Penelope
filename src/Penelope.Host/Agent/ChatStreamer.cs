using Penelope.Abstractions;

namespace Penelope.Host.Agent;

static class ChatStreamer
{
    public static async IAsyncEnumerable<ChatStreamEvent> CompleteAsync(
        IModelProvider provider,
        string model,
        IReadOnlyList<ModelMessage> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = new List<ModelMessage> { new("system", ChatPrompts.Chat, null, null) };
        messages.AddRange(history);
        var any = false;
        await foreach (var ev in provider.CompleteAsync(new ModelCompletion(model, messages, null), cancellationToken))
        {
            if (ev.Kind == ModelEventKind.Token && !string.IsNullOrEmpty(ev.Text))
            {
                any = true;
                yield return ChatStreamEvent.Token(ev.Text);
            }
            else if (ev.Kind == ModelEventKind.Error)
            {
                yield return ChatStreamEvent.Error(ev.Text ?? "The model didn't answer.");
                yield return ChatStreamEvent.Done();
                yield break;
            }
        }

        if (!any)
        {
            yield return ChatStreamEvent.Error("The model returned an empty answer.");
        }

        yield return ChatStreamEvent.Done();
    }
}
