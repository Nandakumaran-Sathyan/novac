using System.ComponentModel.DataAnnotations;

namespace SmartPOS.Application.AI;

/// <summary>One earlier turn of the conversation, sent back by the browser for context.</summary>
public sealed class AIChatTurn
{
    /// <summary>
    /// "user" or "assistant" only. The browser can never send a system prompt or a tool result: the server owns
    /// the instructions, and data is always fetched fresh, so a forged result cannot feed the model fake figures.
    /// </summary>
    [Required, RegularExpression("^(user|assistant)$", ErrorMessage = "Role must be 'user' or 'assistant'.")]
    public string Role { get; init; } = string.Empty;

    [Required, StringLength(4000, MinimumLength = 1)]
    public string Content { get; init; } = string.Empty;
}

public sealed class AIChatRequest
{
    /// <summary>The new question.</summary>
    [Required, StringLength(1000, MinimumLength = 1, ErrorMessage = "Message must be 1-1000 characters.")]
    public string Message { get; init; } = string.Empty;

    /// <summary>Earlier turns, oldest first. Only the most recent ones are used (Assistant:MaxHistoryMessages).</summary>
    [MaxLength(40, ErrorMessage = "History may hold at most 40 turns.")]
    public List<AIChatTurn> History { get; init; } = [];
}

/// <summary>
/// The assistant's answer (may contain simple Markdown), which data tools ran to produce it (empty until the
/// tools are connected), whether the model hit its output limit, and how long it took.
/// </summary>
public sealed record AIChatResponse(string Reply, IReadOnlyList<string> ToolsUsed, bool Truncated, long DurationMs);

/// <summary>Bound from the "Assistant" configuration section.</summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>How many earlier turns are passed to the model. Older ones are dropped (the model's context is small).</summary>
    [Range(0, 40)]
    public int MaxHistoryMessages { get; set; } = 12;

    /// <summary>Upper bound on the characters of history passed to the model, newest kept first.</summary>
    [Range(0, 50_000)]
    public int MaxHistoryCharacters { get; set; } = 8_000;

    /// <summary>
    /// How many times the model may ask for tools while answering one question. Each round costs a full model
    /// call (10-20 s on the development Mac), so this also bounds the worst-case answer time.
    /// </summary>
    [Range(1, 5)]
    public int MaxToolRounds { get; set; } = 3;

    /// <summary>Tool calls accepted in one round; extra calls are ignored (and logged).</summary>
    [Range(1, 10)]
    public int MaxToolCallsPerRound { get; set; } = 6;

    /// <summary>Questions each user may ask per minute (enforced by the API's rate limiter).</summary>
    [Range(1, 1000)]
    public int RequestsPerMinute { get; set; } = 10;
}

/// <summary>The AI assistant: answers one question in the context of the signed-in user's shop.</summary>
public interface IAIService
{
    Task<AIChatResponse> ChatAsync(AIChatRequest request, CancellationToken cancellationToken);
}
