namespace Penelope.Abstractions;

/// <summary>
/// Identifies a managed tool integration. Plugins implement this alongside <see cref="IToolModule"/>.
/// </summary>
public interface IToolProvider : IToolModule
{
    /// <summary>
    /// Stable provider kind (see <see cref="ToolProviderKinds"/>).
    /// </summary>
    string ProviderKind { get; }
}

public static class ToolProviderKinds
{
    public const string Ollama = "ollama";

    public const string Docker = "docker";

    public const string Odysseus = "odysseus";

    public const string FlashMlFreeToken = "flashml-freetoken";
}
