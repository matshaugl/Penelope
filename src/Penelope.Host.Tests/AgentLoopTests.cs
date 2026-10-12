using Penelope.Abstractions;
using Penelope.Host.Agent;
using Penelope.Host.Plugins;
using Xunit;

namespace Penelope.Host.Tests;

public class AgentLoopTests
{
    [Fact]
    public async Task Chat_does_not_send_tools()
    {
        var model = new ScriptedModel(
        [
            new ModelEvent(ModelEventKind.Token, "Hello", null, null, null),
            new ModelEvent(ModelEventKind.Done, null, null, null, null),
        ]);

        var events = await Collect(ChatStreamer.CompleteAsync(model, "tiny", [User("Hi")], CancellationToken.None));

        Assert.Null(model.Seen[0].Tools);
        Assert.Equal("Hello", string.Concat(events.Where(ev => ev.Name == "token").Select(ev => ev.Text)));
        Assert.Equal("done", events[^1].Name);
    }

    [Fact]
    public async Task Agent_calls_web_search_then_answers()
    {
        var tool = new RecordingTool("web_search");
        var model = new ScriptedModel(
        [
            new ModelEvent(ModelEventKind.ToolCall, null, "web_search", """{"query":"penelope"}""", "call-1"),
            new ModelEvent(ModelEventKind.Done, null, null, null, null),
        ],
        [
            new ModelEvent(ModelEventKind.Token, "It works.", null, null, null),
            new ModelEvent(ModelEventKind.Done, null, null, null, null),
        ]);

        var events = await Collect(AgentRunner.RunAsync(model, "tiny", [User("Look it up")], [tool], CancellationToken.None));

        Assert.Equal(["""{"query":"penelope"}"""], tool.Calls);
        Assert.Contains(events, ev => ev.Name == "status" && ev.Text == "Searching the web…");
        Assert.Contains(events, ev => ev.Text == "It works.");
        Assert.Contains(model.Seen[1].Messages, message => message.Role == "tool" && message.Content.Contains("result for web_search"));
        Assert.Equal("web_search", model.Seen[0].Tools![0].Name);
    }

    [Fact]
    public async Task Unknown_tools_are_not_executed()
    {
        var tool = new RecordingTool("web_search");
        var model = new ScriptedModel(
        [
            new ModelEvent(ModelEventKind.ToolCall, null, "filesystem", "{}", "call-1"),
            new ModelEvent(ModelEventKind.Done, null, null, null, null),
        ],
        [
            new ModelEvent(ModelEventKind.Token, "ok", null, null, null),
            new ModelEvent(ModelEventKind.Done, null, null, null, null),
        ]);

        await Collect(AgentRunner.RunAsync(model, "tiny", [User("read a file")], [tool], CancellationToken.None));

        Assert.Empty(tool.Calls);
        Assert.Contains(model.Seen[1].Messages, message => message.Content == "That tool is not available.");
    }

    [Fact]
    public async Task The_agent_stops_after_three_searches()
    {
        var tool = new RecordingTool("web_search");
        var call = new IReadOnlyList<ModelEvent>[]
        {
            [new ModelEvent(ModelEventKind.ToolCall, null, "web_search", """{"query":"a"}""", "1"), new ModelEvent(ModelEventKind.Done, null, null, null, null)],
            [new ModelEvent(ModelEventKind.ToolCall, null, "web_search", """{"query":"b"}""", "2"), new ModelEvent(ModelEventKind.Done, null, null, null, null)],
            [new ModelEvent(ModelEventKind.ToolCall, null, "web_search", """{"query":"c"}""", "3"), new ModelEvent(ModelEventKind.Done, null, null, null, null)],
            [new ModelEvent(ModelEventKind.Token, "Enough.", null, null, null), new ModelEvent(ModelEventKind.Done, null, null, null, null)],
        };
        var model = new ScriptedModel(call);

        var events = await Collect(AgentRunner.RunAsync(model, "tiny", [User("search")], [tool], CancellationToken.None));

        Assert.Equal(3, tool.Calls.Count);
        Assert.Null(model.Seen[3].Tools);
        Assert.Contains(events, ev => ev.Text == "Enough.");
    }

    [Fact]
    public void A_plugin_tool_is_registered_and_duplicates_are_kept_once()
    {
        var catalog = new PluginCatalog(
        [
            new LoadedPlugin
            {
                Id = "extra",
                DisplayName = "Extra",
                Summary = "Extra",
                Module = new ExtraToolModule(),
                Directory = "extra",
            },
        ]);
        var registry = new AgentToolRegistry(
            [new ToolSource(new RecordingTool("web_search"), new RecordingTool("web_search"))],
            catalog);

        Assert.Equal(["web_search", "diagnostics"], registry.Tools.Select(tool => tool.Name).ToArray());
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("system")]
    public void The_page_cannot_send_tool_or_system_turns(string role)
    {
        var error = ChatTranscript.Read(new ChatBody("tiny", [new ChatMessageBody(role, "hello")]), out _, out _);
        Assert.Equal("This page can only send the conversation.", error);
    }

    [Fact]
    public void An_empty_message_is_refused()
    {
        var error = ChatTranscript.Read(new ChatBody(null, [new ChatMessageBody("user", "  ")]), out _, out _);
        Assert.Equal("Enter a message.", error);
    }

    private static ModelMessage User(string content) => new("user", content, null, null);

    private static async Task<List<ChatStreamEvent>> Collect(IAsyncEnumerable<ChatStreamEvent> events)
    {
        var list = new List<ChatStreamEvent>();
        await foreach (var ev in events)
        {
            list.Add(ev);
        }

        return list;
    }

    private sealed class ScriptedModel : IModelProvider
    {
        private readonly Queue<IReadOnlyList<ModelEvent>> _rounds;

        public ScriptedModel(params IReadOnlyList<ModelEvent>[] rounds)
        {
            _rounds = new Queue<IReadOnlyList<ModelEvent>>(rounds);
        }

        public List<ModelCompletion> Seen { get; } = [];

        public string ProviderId => "script";

        public Task<ModelAvailability> AvailabilityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ModelAvailability(true, "Script", "tiny", ["tiny"], null));
        }

        public async IAsyncEnumerable<ModelEvent> CompleteAsync(
            ModelCompletion request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Seen.Add(request);
            foreach (var ev in _rounds.Dequeue())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return ev;
                await Task.Yield();
            }
        }
    }

    private sealed class RecordingTool : IAgentTool
    {
        public RecordingTool(string name)
        {
            Name = name;
        }

        public List<string> Calls { get; } = [];

        public string Name { get; }

        public string Description => "test";

        public string ParametersSchema => """{"type":"object"}""";

        public Task<string> InvokeAsync(string argumentsJson, CancellationToken cancellationToken)
        {
            Calls.Add(argumentsJson);
            return Task.FromResult("result for " + Name);
        }
    }

    private sealed class ToolSource : IAgentToolSource
    {
        public ToolSource(params IAgentTool[] tools)
        {
            Tools = tools;
        }

        public IReadOnlyList<IAgentTool> Tools { get; }
    }

    private sealed class ExtraToolModule : IToolModule, IAgentToolSource
    {
        public string Id => "extra";

        public string DisplayName => "Extra";

        public string Summary => "Extra";

        public ToolAvailability Availability => ToolAvailability.Ready;

        public IReadOnlyList<ModuleNavItem> Nav => [];

        public ToolStatus GetStatus() => new(ToolRunState.Stopped, "Stopped");

        public ToolRelease GetRelease() => new(null, null, null, false, null);

        public Task CheckForUpdateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public InstallPlan PlanInstall() => new([]);

        public Task<ModuleActionResult> InstallAsync(IJobLog log, CancellationToken cancellationToken)
            => Task.FromResult(new ModuleActionResult(true, null));

        public Task<ModuleActionResult> UpgradeAsync(IJobLog log, CancellationToken cancellationToken)
            => Task.FromResult(new ModuleActionResult(true, null));

        public Task<ModuleActionResult> StartAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ModuleActionResult(true, null));

        public Task<ModuleActionResult> StopAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ModuleActionResult(true, null));

        public void ContributeApi(IModuleEndpointBuilder endpoints)
        {
        }

        public IReadOnlyList<IAgentTool> Tools { get; } = [new RecordingTool("diagnostics")];
    }
}
