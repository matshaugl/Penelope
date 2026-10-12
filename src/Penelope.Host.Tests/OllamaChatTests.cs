using System.Net;
using System.Text;
using Penelope.Abstractions;
using Penelope.Modules.Ollama;
using Xunit;

namespace Penelope.Host.Tests;

public class OllamaChatTests
{
    [Fact]
    public void A_token_line_and_a_tool_call_line_parse()
    {
        var token = OllamaChatLines.Read("""{"message":{"role":"assistant","content":"Hi"},"done":false}""");
        Assert.Equal("Hi", token!.Text);
        Assert.False(token.Done);

        var call = OllamaChatLines.Read("""{"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"web_search","arguments":{"query":"penelope"}}}]},"done":true}""");
        Assert.True(call!.Done);
        Assert.Equal("web_search", call.Tools[0].Name);
        Assert.Contains("penelope", call.Tools[0].Arguments);
        Assert.False(call.Tools[0].AppendArguments);
    }

    [Fact]
    public void Tool_support_and_missing_models_become_sentences()
    {
        Assert.Equal(
            "This model can't use Agent mode. Switch to Chat, or try another model.",
            OllamaChatLines.Friendly("registry.ollama.ai/library/qwen2.5:0.5b does not support tools"));
        Assert.Equal("That model isn't installed.", OllamaChatLines.Friendly("model 'missing:latest' not found"));
        Assert.Equal("The model didn't answer.", OllamaChatLines.Friendly("traceback: something failed"));
    }

    [Fact]
    public async Task A_stream_yields_tokens_and_omits_tools_when_chat_does_not_ask()
    {
        var handler = new ScriptHandler("""
            {"message":{"role":"assistant","content":"Hi"},"done":false}
            {"message":{"role":"assistant","content":" there"},"done":true}
            """);
        var client = new OllamaClient(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:9/") });
        var events = new List<ModelEvent>();
        await foreach (var ev in client.ChatAsync(new ModelCompletion("tiny", [new ModelMessage("user", "Hello", null, null)], null), CancellationToken.None))
        {
            events.Add(ev);
        }

        Assert.DoesNotContain("\"tools\"", handler.Body);
        Assert.Equal("Hi there", string.Concat(events.Where(ev => ev.Kind == ModelEventKind.Token).Select(ev => ev.Text)));
        Assert.Equal(ModelEventKind.Done, events[^1].Kind);
    }

    [Fact]
    public async Task Split_tool_arguments_are_joined_before_the_call_is_emitted()
    {
        var handler = new ScriptHandler("""
            {"message":{"content":"","tool_calls":[{"function":{"name":"web_search","arguments":"{\"que"}}]},"done":false}
            {"message":{"content":"","tool_calls":[{"function":{"index":0,"arguments":"ry\":\"x\"}"}}]},"done":true}
            """);
        var client = new OllamaClient(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:9/") });
        var events = new List<ModelEvent>();
        await foreach (var ev in client.ChatAsync(
            new ModelCompletion("tiny", [new ModelMessage("user", "Search", null, null)], [new ModelToolDefinition("web_search", "Search", """{"type":"object"}""")]),
            CancellationToken.None))
        {
            events.Add(ev);
        }

        Assert.Contains("\"tools\"", handler.Body);
        var call = Assert.Single(events, ev => ev.Kind == ModelEventKind.ToolCall);
        Assert.Equal("""{"query":"x"}""", call.ArgumentsJson);
    }

    [Fact]
    public async Task A_model_that_cannot_use_tools_returns_the_sentence()
    {
        var handler = new ScriptHandler("""{"error":"model does not support tools"}""", HttpStatusCode.BadRequest);
        var client = new OllamaClient(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:9/") });
        var events = new List<ModelEvent>();
        await foreach (var ev in client.ChatAsync(new ModelCompletion("tiny", [new ModelMessage("user", "Hi", null, null)], []), CancellationToken.None))
        {
            events.Add(ev);
        }

        Assert.Equal("This model can't use Agent mode. Switch to Chat, or try another model.", events[0].Text);
    }

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public ScriptHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
