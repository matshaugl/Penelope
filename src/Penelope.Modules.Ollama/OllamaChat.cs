using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Penelope.Abstractions;

namespace Penelope.Modules.Ollama;

static class OllamaChat
{
    public static async IAsyncEnumerable<ModelEvent> CompleteAsync(
        HttpClient http,
        ModelCompletion request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new StringContent(Body(request).ToJsonString(), Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage? response = null;
        string? failure = null;
        var aborted = false;
        try
        {
            response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            aborted = cancellationToken.IsCancellationRequested;
            failure = aborted ? null : "The model took too long to answer.";
        }
        catch (HttpRequestException)
        {
            failure = "The model didn't answer.";
        }

        if (aborted)
        {
            yield break;
        }

        if (failure is not null)
        {
            yield return new ModelEvent(ModelEventKind.Error, failure, null, null, null);
            yield break;
        }

        using (response)
        {
            if (response is null)
            {
                yield break;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                yield return new ModelEvent(ModelEventKind.Error, OllamaChatLines.Friendly(ErrorText(body)), null, null, null);
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);
            var pending = new SortedDictionary<int, PendingCall>();
            while (true)
            {
                string? line = null;
                failure = null;
                aborted = false;
                try
                {
                    line = await reader.ReadLineAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    aborted = cancellationToken.IsCancellationRequested;
                    failure = aborted ? null : "The model took too long to answer.";
                }

                if (aborted)
                {
                    yield break;
                }

                if (failure is not null)
                {
                    yield return new ModelEvent(ModelEventKind.Error, failure, null, null, null);
                    yield break;
                }

                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parsed = OllamaChatLines.Read(line);
                if (parsed is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(parsed.Error))
                {
                    yield return new ModelEvent(ModelEventKind.Error, OllamaChatLines.Friendly(parsed.Error), null, null, null);
                    yield break;
                }

                if (!string.IsNullOrEmpty(parsed.Text))
                {
                    yield return new ModelEvent(ModelEventKind.Token, parsed.Text, null, null, null);
                }

                foreach (var delta in parsed.Tools)
                {
                    Apply(pending, delta);
                }

                if (parsed.Done)
                {
                    foreach (var pair in pending)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Value.Name))
                        {
                            continue;
                        }

                        var arguments = pair.Value.Arguments.Length == 0 ? "{}" : pair.Value.Arguments.ToString();
                        var id = string.IsNullOrWhiteSpace(pair.Value.Id) ? $"call-{pair.Key + 1}" : pair.Value.Id;
                        yield return new ModelEvent(ModelEventKind.ToolCall, null, pair.Value.Name, arguments, id);
                    }

                    yield return new ModelEvent(ModelEventKind.Done, null, null, null, null);
                    yield break;
                }
            }
        }

        yield return new ModelEvent(ModelEventKind.Done, null, null, null, null);
    }

    private static void Apply(SortedDictionary<int, PendingCall> pending, OllamaToolDelta delta)
    {
        if (!pending.TryGetValue(delta.Index, out var call))
        {
            call = new PendingCall();
            pending[delta.Index] = call;
        }

        if (!string.IsNullOrWhiteSpace(delta.Id))
        {
            call.Id = delta.Id;
        }

        if (!string.IsNullOrWhiteSpace(delta.Name))
        {
            call.Name = delta.Name;
        }

        if (delta.Arguments is null)
        {
            return;
        }

        if (delta.AppendArguments)
        {
            if (!call.CompleteObject)
            {
                call.Arguments.Append(delta.Arguments);
            }
        }
        else
        {
            call.Arguments.Clear();
            call.Arguments.Append(delta.Arguments);
            call.CompleteObject = true;
        }
    }

    private static JsonObject Body(ModelCompletion request)
    {
        var payload = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = Messages(request.Messages),
            ["stream"] = true,
        };
        if (request.Tools is not null)
        {
            payload["tools"] = Tools(request.Tools);
        }

        return payload;
    }

    private static JsonArray Messages(IReadOnlyList<ModelMessage> messages)
    {
        var array = new JsonArray();
        foreach (var message in messages)
        {
            var item = new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = message.Content,
            };
            if (message.ToolName is not null)
            {
                item["tool_name"] = message.ToolName;
            }

            if (message.ToolCalls is { Count: > 0 })
            {
                var calls = new JsonArray();
                foreach (var call in message.ToolCalls)
                {
                    JsonNode? arguments;
                    try
                    {
                        arguments = JsonNode.Parse(call.ArgumentsJson);
                    }
                    catch (JsonException)
                    {
                        arguments = call.ArgumentsJson;
                    }

                    calls.Add(new JsonObject
                    {
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = arguments,
                        },
                    });
                }

                item["tool_calls"] = calls;
            }

            array.Add(item);
        }

        return array;
    }

    private static JsonArray Tools(IReadOnlyList<ModelToolDefinition> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            JsonNode? parameters;
            try
            {
                parameters = JsonNode.Parse(tool.ParametersSchema);
            }
            catch (JsonException)
            {
                parameters = new JsonObject { ["type"] = "object" };
            }

            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = parameters,
                },
            });
        }

        return array;
    }

    private static string? ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return body;
    }

    private sealed class PendingCall
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public StringBuilder Arguments { get; } = new();

        public bool CompleteObject { get; set; }
    }
}

internal sealed record OllamaChatLine(string? Text, IReadOnlyList<OllamaToolDelta> Tools, bool Done, string? Error);

internal sealed record OllamaToolDelta(int Index, string? Id, string? Name, string? Arguments, bool AppendArguments);

static class OllamaChatLines
{
    public static OllamaChatLine? Read(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            string? error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            var done = root.TryGetProperty("done", out var doneElement) && doneElement.ValueKind == JsonValueKind.True;
            string? text = null;
            var tools = new List<OllamaToolDelta>();
            if (root.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    text = content.GetString();
                }

                if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var call in calls.EnumerateArray())
                    {
                        var function = call.TryGetProperty("function", out var functionElement) ? functionElement : call;
                        var name = function.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                            ? nameElement.GetString()
                            : null;
                        var id = call.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                            ? idElement.GetString()
                            : null;
                        string? arguments = null;
                        var append = false;
                        if (function.TryGetProperty("arguments", out var argumentsElement))
                        {
                            if (argumentsElement.ValueKind == JsonValueKind.String)
                            {
                                arguments = argumentsElement.GetString();
                                append = true;
                            }
                            else if (argumentsElement.ValueKind == JsonValueKind.Object)
                            {
                                arguments = argumentsElement.GetRawText();
                            }
                        }

                        var callIndex = function.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out var parsed)
                            ? parsed
                            : index;
                        tools.Add(new OllamaToolDelta(callIndex, id, name, arguments, append));
                        index++;
                    }
                }
            }

            return new OllamaChatLine(text, tools, done, error);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Friendly(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "The model didn't answer.";
        }

        if (error.Contains("does not support tools", StringComparison.OrdinalIgnoreCase))
        {
            return "This model can't use Agent mode. Switch to Chat, or try another model.";
        }

        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "That model isn't installed.";
        }

        return "The model didn't answer.";
    }
}
