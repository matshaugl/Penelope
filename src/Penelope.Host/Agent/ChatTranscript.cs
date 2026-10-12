using Penelope.Abstractions;

namespace Penelope.Host.Agent;

sealed record ChatBody(string? Model, List<ChatMessageBody>? Messages);

sealed record ChatMessageBody(string? Role, string? Content);

static class ChatTranscript
{
    public const int MaxMessages = 20;

    public const int MaxCharacters = 4000;

    public static string? Read(ChatBody? body, out List<ModelMessage> messages, out string? model)
    {
        messages = [];
        model = null;
        if (body?.Messages is null || body.Messages.Count == 0)
        {
            return "Enter a message.";
        }

        if (body.Messages.Count > MaxMessages)
        {
            return "This conversation is too long. Clear it and start again.";
        }

        if (!string.IsNullOrWhiteSpace(body.Model))
        {
            model = CleanModel(body.Model);
            if (model is null)
            {
                return "That model name is not valid.";
            }
        }

        foreach (var message in body.Messages)
        {
            var role = message.Role?.Trim().ToLowerInvariant();
            if (role is not ("user" or "assistant"))
            {
                return "This page can only send the conversation.";
            }

            var content = message.Content?.Trim() ?? "";
            if (content.Length == 0 || content.Length > MaxCharacters)
            {
                return content.Length == 0 ? "Enter a message." : "That message is too long.";
            }

            messages.Add(new ModelMessage(role, content, null, null));
        }

        if (messages[^1].Role != "user")
        {
            return "Enter a message.";
        }

        return null;
    }

    public static string? CleanModel(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 200)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            var ok = char.IsAsciiLetterOrDigit(character) || character is ':' or '.' or '_' or '-' or '/';
            if (!ok)
            {
                return null;
            }
        }

        return trimmed;
    }
}
