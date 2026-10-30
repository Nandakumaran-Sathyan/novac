using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPOS.Application.AI.Tools;
using SmartPOS.Application.Common;

namespace SmartPOS.Application.AI;

/// <summary>
/// Answers one question for the signed-in user:
/// <list type="number">
/// <item>Builds the conversation: the server's system prompt, the trimmed history, the question.</item>
/// <item>Lets the model call the read-only store tools for up to <see cref="AssistantOptions.MaxToolRounds"/> rounds.
/// Every call goes through <see cref="StoreToolRegistry"/> (argument checks, shop scoping by the database filter).</item>
/// <item>Checks the final answer with <see cref="GroundingCheck"/>: a figure that is not in this question's tool
/// results gets one retry with a reminder; if it is still there, the answer is withheld.</item>
/// </list>
/// The shop comes only from the token (<see cref="ICurrentUser"/>). Questions, answers and tool data are never logged.
/// </summary>
public sealed class AIOrchestrationService(
    IChatModelClient model,
    StoreToolRegistry toolRegistry,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    StoreCalendar calendar,
    AIConcurrencyGate gate,
    IOptions<AssistantOptions> assistantOptions,
    IOptions<StoreOptions> storeOptions,
    ILogger<AIOrchestrationService> logger) : IAIService
{
    public const string NoAnswer = "I couldn't put an answer together for that. Please try rephrasing the question.";

    public const string Unverified =
        "I couldn't verify the figures for that answer against your shop's data, so I won't show them. " +
        "Please try asking a more specific question, for example \"What were yesterday's sales?\".";

    public async Task<AIChatResponse> ChatAsync(AIChatRequest request, CancellationToken cancellationToken)
    {
        using var lease = gate.TryEnter(currentUser.UserId)
                          ?? throw new TooManyRequestsException("Your previous question is still being answered. Please wait for it to finish.");

        var options = assistantOptions.Value;
        var question = request.Message.Trim();
        var shopName = await db.Shops.AsNoTracking()
            .Where(s => s.Id == currentUser.ShopId)
            .Select(s => s.ShopName)
            .SingleOrDefaultAsync(cancellationToken) ?? "your shop";

        var tools = StoreToolRegistry.Definitions;
        var systemPrompt = AssistantPrompt.Build(shopName, calendar.Today, storeOptions.Value.CurrencyCode, toolsAvailable: tools.Count > 0);
        var messages = new List<ChatMessage> { ChatMessage.System(systemPrompt) };
        messages.AddRange(TrimHistory(request.History, options));
        messages.Add(ChatMessage.User(question));

        var run = new Run();
        var stopwatch = Stopwatch.StartNew();

        var answer = await AnswerAsync(messages, tools, options, run, cancellationToken);
        var unsupported = Unsupported(answer.Text, systemPrompt, question, run);
        if (unsupported.Count > 0)
        {
            // One retry, with a reminder. The model may call tools now if it skipped them.
            run.GroundingRetries++;
            logger.LogWarning("AI answer had {Count} figure(s) not found in tool results (tools used: {Tools}); retrying once",
                unsupported.Count, run.ToolsUsed.Count);
            messages.Add(new ChatMessage(ChatRole.Assistant, answer.Text));
            messages.Add(ChatMessage.User(GroundingReminder(unsupported, run.ToolsUsed.Count > 0)));
            answer = await AnswerAsync(messages, tools, options, run, cancellationToken);
            unsupported = Unsupported(answer.Text, systemPrompt, question, run);
        }

        var reply = answer.Text;
        var verified = unsupported.Count == 0;
        if (!verified)
        {
            logger.LogWarning("AI answer withheld: {Count} unverified figure(s) after a retry", unsupported.Count);
            reply = Unverified;
        }
        else if (reply.Length == 0)
        {
            reply = NoAnswer;
        }
        stopwatch.Stop();

        logger.LogInformation(
            "AI chat for shop {ShopId} user {UserId}: {Rounds} model call(s), tools [{Tools}], grounding retries {Retries}, verified {Verified}, {DurationMs} ms",
            currentUser.ShopId, currentUser.UserId, run.ModelCalls, string.Join(", ", run.ToolsUsed), run.GroundingRetries, verified, stopwatch.ElapsedMilliseconds);

        return new AIChatResponse(reply, run.ToolsUsed.Distinct().ToList(), answer.Truncated, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>What happened while answering: used for grounding (which tool results exist) and for logging.</summary>
    private sealed class Run
    {
        public List<string> ToolsUsed { get; } = [];
        public List<string> ToolResults { get; } = [];
        public int ModelCalls { get; set; }
        public int GroundingRetries { get; set; }
    }

    private sealed record Answer(string Text, bool Truncated);

    /// <summary>Runs the model, executing any tools it asks for, until it gives a text answer or the rounds run out.</summary>
    private async Task<Answer> AnswerAsync(
        List<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, AssistantOptions options, Run run, CancellationToken cancellationToken)
    {
        for (var round = 0; ; round++)
        {
            // Out of rounds: ask once more without offering tools, so the model must answer from what it has.
            var offer = round < options.MaxToolRounds ? tools : [];
            var response = await model.CompleteAsync(messages, offer, cancellationToken);
            run.ModelCalls++;

            var calls = response.Message.ToolCalls;
            if (calls is not { Count: > 0 } || offer.Count == 0)
                return new Answer(response.Message.Content.Trim(), response.Truncated);

            if (calls.Count > options.MaxToolCallsPerRound)
                logger.LogWarning("AI asked for {Count} tools in one round; only the first {Max} run", calls.Count, options.MaxToolCallsPerRound);
            var accepted = calls.Take(options.MaxToolCallsPerRound).ToList();

            messages.Add(new ChatMessage(ChatRole.Assistant, response.Message.Content, accepted));
            foreach (var call in accepted)
            {
                var result = await toolRegistry.ExecuteAsync(call, cancellationToken);
                if (result.Success)
                {
                    run.ToolsUsed.Add(result.Name);
                    run.ToolResults.Add(result.Json);
                }
                messages.Add(ChatMessage.ToolResult(call.Name, result.Json));
            }
        }
    }

    /// <summary>Trusted sources for this question only: tool results, the server's prompt, the user's own question.</summary>
    private static IReadOnlyList<string> Unsupported(string answer, string systemPrompt, string question, Run run) =>
        GroundingCheck.FindUnsupportedNumbers(answer, [systemPrompt, question, .. run.ToolResults]);

    private static string GroundingReminder(IReadOnlyList<string> unsupported, bool toolsWereUsed) =>
        (toolsWereUsed
            ? $"Your answer contains figures that are not in the tool results: {string.Join(", ", unsupported.Take(8))}. "
            : $"You answered with figures ({string.Join(", ", unsupported.Take(8))}) without calling any tool. ") +
        "Call the matching tool(s) if you have not, then answer again using ONLY figures from the tool results. " +
        "Do not calculate new figures. If the tools do not provide the answer, say so.";

    /// <summary>
    /// Keeps the most recent turns that fit both limits, in their original order. The model's context is small,
    /// and older turns matter least.
    /// </summary>
    internal static IEnumerable<ChatMessage> TrimHistory(IReadOnlyList<AIChatTurn> history, AssistantOptions options)
    {
        var kept = new List<ChatMessage>();
        var characters = 0;
        for (var i = history.Count - 1; i >= 0 && kept.Count < options.MaxHistoryMessages; i--)
        {
            var turn = history[i];
            characters += turn.Content.Length;
            if (characters > options.MaxHistoryCharacters)
                break;
            kept.Add(turn.Role == "assistant" ? new ChatMessage(ChatRole.Assistant, turn.Content) : ChatMessage.User(turn.Content));
        }
        kept.Reverse();
        return kept;
    }
}
