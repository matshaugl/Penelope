namespace Penelope.Host.Agent;

sealed record ChatStreamEvent(string Name, string? Text)
{
    public static ChatStreamEvent Token(string text) => new("token", text);

    public static ChatStreamEvent Status(string text) => new("status", text);

    public static ChatStreamEvent Done() => new("done", null);

    public static ChatStreamEvent Error(string text) => new("error", text);
}
