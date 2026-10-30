using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPOS.Application.AI;

namespace SmartPOS.Infrastructure.AI;

/// <summary>
/// Talks to Ollama's native <c>/api/chat</c> endpoint with tool calling. The request and response shapes below
/// were verified against qwen3.5:9b on Ollama 0.40.13 (see docs/AI.md): tool calls arrive in
/// <c>message.tool_calls[].function</c> with <c>arguments</c> as a JSON object, and tool results are sent back as
/// <c>{ role: "tool", tool_name, content }</c>. Message text and tool results are never written to the log,
/// because they contain shop data.
/// </summary>
public sealed class OllamaChatClient(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaChatClient> logger)
    : IChatModelClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public async Task<ChatModelResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var request = new WireChatRequest(
            o.Model,
            messages.Select(ToWire).ToList(),
            tools.Count == 0 ? null : tools.Select(t => new WireTool("function", new WireFunction(t.Name, t.Description, t.ParametersSchema))).ToList(),
            Stream: false,
            Think: o.Think,
            KeepAlive: o.KeepAlive,
            Options: new WireOptions(o.Temperature, o.ContextLength));

        WireChatResponse response;
        try
        {
            using var reply = await http.PostAsJsonAsync("api/chat", request, Json, cancellationToken);
            if (reply.StatusCode == HttpStatusCode.NotFound)
                throw Unavailable($"The AI model '{o.Model}' is not installed. Run: ollama pull {o.Model}");
            if (!reply.IsSuccessStatusCode)
            {
                logger.LogError("Ollama returned {Status}: {Body}", (int)reply.StatusCode, Truncate(await reply.Content.ReadAsStringAsync(cancellationToken)));
                throw Unavailable("The AI model returned an error. Try again in a moment.");
            }
            response = await reply.Content.ReadFromJsonAsync<WireChatResponse>(Json, cancellationToken)
                       ?? throw Unavailable("The AI model returned an empty reply.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller gave up (e.g. the browser disconnected); not an error
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Ollama call exceeded {Timeout}s", o.TimeoutSeconds);
            throw Unavailable("The AI model took too long to answer. Try a shorter question, or try again.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Ollama is not reachable at {BaseUrl}", o.BaseUrl);
            throw Unavailable("The AI assistant is offline. Start Ollama and try again.");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Ollama reply could not be parsed");
            throw Unavailable("The AI model returned a reply that could not be read.");
        }

        var toolCalls = response.Message?.ToolCalls?
            .Where(c => !string.IsNullOrWhiteSpace(c.Function?.Name))
            .Select(c => new ToolCall(c.Function!.Name!, NormalizeArguments(c.Function.Arguments, c.Function.Name!)))
            .ToList();
        var usage = new ChatModelUsage(response.PromptEvalCount, response.EvalCount, TimeSpan.FromTicks(response.TotalDuration / 100));

        logger.LogInformation(
            "Ollama {Model}: {DurationMs} ms, {PromptTokens} prompt + {OutputTokens} output tokens, tool calls [{Tools}], done: {DoneReason}",
            o.Model, (long)usage.Duration.TotalMilliseconds, usage.PromptTokens, usage.OutputTokens,
            string.Join(", ", toolCalls?.Select(c => c.Name) ?? []), response.DoneReason);

        return new ChatModelResponse(
            new ChatMessage(ChatRole.Assistant, response.Message?.Content ?? string.Empty, toolCalls is { Count: > 0 } ? toolCalls : null),
            Truncated: response.DoneReason == "length",
            usage);
    }

    public async Task<ChatModelStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var model = options.Value.Model;
        string? version;
        try
        {
            version = (await http.GetFromJsonAsync<WireVersion>("api/version", Json, cancellationToken))?.Version;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama status check: not reachable");
            return new ChatModelStatus(false, model, false, false, null, "Ollama is not running.");
        }

        try
        {
            using var reply = await http.PostAsJsonAsync("api/show", new { model }, Json, cancellationToken);
            if (reply.StatusCode == HttpStatusCode.NotFound)
                return new ChatModelStatus(true, model, false, false, version, $"The model '{model}' is not installed.");
            reply.EnsureSuccessStatusCode();
            var show = await reply.Content.ReadFromJsonAsync<WireShow>(Json, cancellationToken);
            var tools = show?.Capabilities?.Contains("tools") == true;
            return new ChatModelStatus(true, model, true, tools, version, tools ? null : $"The model '{model}' does not support tool calling.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama status check: model lookup failed");
            return new ChatModelStatus(true, model, false, false, version, "Could not read the model's details.");
        }
    }

    /// <summary>
    /// Ollama sends arguments as a JSON object, but some models emit them as a JSON string; accept both.
    /// Anything else becomes an empty object, and argument validation (not the model) decides what happens next.
    /// </summary>
    private JsonElement NormalizeArguments(JsonElement? arguments, string tool)
    {
        if (arguments is not { } value)
            return EmptyObject;
        if (value.ValueKind == JsonValueKind.Object)
            return value.Clone();
        if (value.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var parsed = JsonDocument.Parse(value.GetString() ?? "{}");
                if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                    return parsed.RootElement.Clone();
            }
            catch (JsonException)
            {
                // fall through
            }
        }
        logger.LogWarning("Tool call {Tool} had unusable arguments ({Kind}); treating them as empty", tool, value.ValueKind);
        return EmptyObject;
    }

    private static WireMessage ToWire(ChatMessage m) => new(
        m.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            _ => "tool"
        },
        m.Content,
        m.ToolCalls?.Select(c => new WireToolCall(new WireToolCallFunction(c.Name, c.Arguments))).ToList(),
        m.ToolName);

    private static AIUnavailableException Unavailable(string message) => new(message);

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "...";

    // ---- Ollama wire format (snake_case where Ollama uses it) ---------------------------------------------

    private sealed record WireChatRequest(
        string Model,
        List<WireMessage> Messages,
        List<WireTool>? Tools,
        bool Stream,
        bool Think,
        [property: JsonPropertyName("keep_alive")] string KeepAlive,
        WireOptions Options);

    private sealed record WireOptions(double Temperature, [property: JsonPropertyName("num_ctx")] int NumCtx);

    private sealed record WireMessage(
        string Role,
        string Content,
        [property: JsonPropertyName("tool_calls")] List<WireToolCall>? ToolCalls,
        [property: JsonPropertyName("tool_name")] string? ToolName);

    private sealed record WireToolCall(WireToolCallFunction Function);

    private sealed record WireToolCallFunction(string Name, JsonElement Arguments);

    private sealed record WireTool(string Type, WireFunction Function);

    private sealed record WireFunction(string Name, string Description, JsonElement Parameters);

    private sealed record WireChatResponse(
        WireReplyMessage? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int EvalCount,
        [property: JsonPropertyName("total_duration")] long TotalDuration);

    private sealed record WireReplyMessage(
        string? Role,
        string? Content,
        [property: JsonPropertyName("tool_calls")] List<WireReplyToolCall>? ToolCalls);

    private sealed record WireReplyToolCall(WireReplyFunction? Function);

    private sealed record WireReplyFunction(string? Name, JsonElement? Arguments);

    private sealed record WireVersion(string? Version);

    private sealed record WireShow(List<string>? Capabilities);
}
