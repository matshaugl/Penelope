namespace Penelope.Host.Agent;

static class ChatPrompts
{
    public const string Chat =
        "You are answering a short test inside Penelope. The person wants to see that this model works. Reply in plain language and keep it brief.";

    public const string Agent =
        "You are answering a short test inside Penelope. The person wants to see that this model works. You can call web_search when you need public information from the web. After the search, answer in a few plain sentences. If the search fails, say that. You cannot open files or change this computer.";
}
