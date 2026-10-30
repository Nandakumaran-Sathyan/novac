using SmartPOS.Application.AI;

namespace SmartPOS.UnitTests;

public class AssistantTests
{
    private static AIChatTurn Turn(string role, string content) => new() { Role = role, Content = content };

    [Fact]
    public void History_keeps_the_newest_turns_in_order_within_the_turn_limit()
    {
        var history = Enumerable.Range(1, 10).Select(i => Turn(i % 2 == 1 ? "user" : "assistant", $"t{i}")).ToList();
        var kept = AIOrchestrationService.TrimHistory(history, new AssistantOptions { MaxHistoryMessages = 4, MaxHistoryCharacters = 10_000 }).ToList();

        Assert.Equal(["t7", "t8", "t9", "t10"], kept.Select(m => m.Content));
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant], kept.Select(m => m.Role));
    }

    [Fact]
    public void History_stops_at_the_character_budget_dropping_the_oldest()
    {
        var history = new[] { Turn("user", new string('a', 50)), Turn("assistant", new string('b', 50)), Turn("user", new string('c', 50)) };
        var kept = AIOrchestrationService.TrimHistory(history, new AssistantOptions { MaxHistoryMessages = 10, MaxHistoryCharacters = 120 }).ToList();

        Assert.Equal(2, kept.Count);
        Assert.StartsWith("b", kept[0].Content);
        Assert.StartsWith("c", kept[1].Content);
    }

    [Fact]
    public void No_history_is_fine() =>
        Assert.Empty(AIOrchestrationService.TrimHistory([], new AssistantOptions()));

    [Fact]
    public void Without_tools_the_prompt_says_the_data_is_not_available()
    {
        var prompt = AssistantPrompt.Build("Pandian Stores", new DateOnly(2026, 10, 30), "INR", toolsAvailable: false);

        Assert.Contains("\"Pandian Stores\"", prompt);
        Assert.Contains("Friday, 30 October 2026", prompt);
        Assert.Contains("INR", prompt);
        Assert.Contains("CANNOT see this shop's sales", prompt);
        Assert.Contains("Never write a number", prompt);
        Assert.DoesNotContain("call the matching tool", prompt);
    }

    [Fact]
    public void With_tools_the_prompt_demands_tool_calls_for_every_part_of_a_question()
    {
        var prompt = AssistantPrompt.Build("Shop", new DateOnly(2026, 10, 30), "INR", toolsAvailable: true);
        Assert.Contains("call the matching tool(s) first", prompt);
        Assert.Contains("several tools in the same turn", prompt);
        Assert.DoesNotContain("CANNOT see", prompt);
    }

    [Fact]
    public void The_gate_allows_one_question_per_user_and_reopens_after_disposal()
    {
        var gate = new AIConcurrencyGate();
        var lease = gate.TryEnter(1);
        Assert.NotNull(lease);
        Assert.Null(gate.TryEnter(1));          // same user, still busy
        Assert.NotNull(gate.TryEnter(2));        // another user is independent

        lease!.Dispose();
        lease.Dispose();                         // double dispose is harmless
        Assert.NotNull(gate.TryEnter(1));
    }
}
