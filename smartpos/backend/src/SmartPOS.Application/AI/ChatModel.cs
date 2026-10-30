using System.Text.Json;
using SmartPOS.Application.Common;

namespace SmartPOS.Application.AI;

/// <summary>Who wrote a message in a model conversation.</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    /// <summary>The result of a tool the server executed, fed back to the model.</summary>
    Tool
}

/// <summary>A tool the model asked the server to run. <see cref="Arguments"/> is always a JSON object (possibly empty).</summary>
public sealed record ToolCall(string Name, JsonElement Arguments);

/// <summary>
/// One message in a model conversation. <c>ToolCalls</c> is set on assistant messages that request tools;
/// <c>ToolName</c> is set on <see cref="ChatRole.Tool"/> messages to say which tool produced the content.
/// </summary>
public sealed record ChatMessage(ChatRole Role, string Content, IReadOnlyList<ToolCall>? ToolCalls = null, string? ToolName = null)
{
    public static ChatMessage System(string content) => new(ChatRole.System, content);
    public static ChatMessage User(string content) => new(ChatRole.User, content);
    public static ChatMessage ToolResult(string toolName, string json) => new(ChatRole.Tool, json, ToolName: toolName);
}

/// <summary>A tool the model may call: its name, what it does, and a JSON Schema for its arguments.</summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement ParametersSchema);

public sealed record ChatModelUsage(int PromptTokens, int OutputTokens, TimeSpan Duration);

/// <summary>The model's next message (text, tool calls, or both). <c>Truncated</c>: it hit the output limit and may be cut off.</summary>
public sealed record ChatModelResponse(ChatMessage Message, bool Truncated, ChatModelUsage Usage);

/// <summary>
/// Whether the assistant can work: the model server answered, the configured model is downloaded, and it
/// advertises tool calling. <c>Problem</c> is a short, user-safe explanation when something is not ready.
/// </summary>
public sealed record ChatModelStatus(
    bool Reachable, string Model, bool ModelInstalled, bool SupportsTools, string? ServerVersion, string? Problem)
{
    public bool Ready => Reachable && ModelInstalled && SupportsTools;
}

/// <summary>
/// The language model behind the assistant. Implemented by the Ollama client in Infrastructure; tests use a
/// scripted fake. The interface knows nothing about shops or tenants: tools are executed by the server,
/// never by the model, and the model never sees or chooses a shop id.
/// </summary>
public interface IChatModelClient
{
    /// <summary>Sends the conversation (and the tools the model may call) and returns the model's next message.</summary>
    /// <exception cref="AIUnavailableException">The model server is down, too slow, or missing the model.</exception>
    Task<ChatModelResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken);

    /// <summary>Checks the model server and the configured model without generating anything. Never throws for an unreachable server.</summary>
    Task<ChatModelStatus> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 503: the AI model cannot answer right now (server down, too slow, model missing). The message is safe to
/// show to the user; the client logs the technical cause before throwing.
/// </summary>
public sealed class AIUnavailableException(string message) : AppException(message);
