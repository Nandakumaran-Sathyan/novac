using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartPOS.Application.AI;
using SmartPOS.Infrastructure;
using SmartPOS.Infrastructure.AI;

namespace SmartPOS.UnitTests;

/// <summary>
/// Drives the real client through a fake HTTP handler. The canned replies are the shapes recorded from
/// qwen3.5:9b on Ollama 0.40.13, so these tests pin the verified wire format.
/// </summary>
public class OllamaChatClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OllamaChatClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, string, HttpResponseMessage> respond, OllamaOptions? options = null)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        return (new OllamaChatClient(http, Options.Create(options ?? new OllamaOptions()), NullLogger<OllamaChatClient>.Instance), handler);
    }

    private static readonly ToolDefinition TopProducts = new("GetTopSellingProducts", "Best sellers",
        JsonDocument.Parse("""{"type":"object","properties":{"period":{"type":"string"}},"required":["period"]}""").RootElement);

    // Recorded from qwen3.5:9b: arguments arrive as a JSON object, content is empty.
    private const string ToolCallReply = """
        {"model":"qwen3.5:9b","message":{"role":"assistant","content":"","tool_calls":[{"id":"call_uex58509","function":{"index":0,"name":"GetTopSellingProducts","arguments":{"period":"thisMonth","limit":5}}}]},
         "done":true,"done_reason":"stop","total_duration":14100000000,"prompt_eval_count":489,"eval_count":43}
        """;

    [Fact]
    public async Task Parses_a_tool_call_with_object_arguments()
    {
        var (client, _) = Create((_, _) => Json(ToolCallReply));

        var reply = await client.CompleteAsync([ChatMessage.User("Top five this month?")], [TopProducts], CancellationToken.None);

        var call = Assert.Single(reply.Message.ToolCalls!);
        Assert.Equal("GetTopSellingProducts", call.Name);
        Assert.Equal("thisMonth", call.Arguments.GetProperty("period").GetString());
        Assert.Equal(5, call.Arguments.GetProperty("limit").GetInt32());
        Assert.Equal(489, reply.Usage.PromptTokens);
        Assert.Equal(43, reply.Usage.OutputTokens);
        Assert.Equal(14.1, reply.Usage.Duration.TotalSeconds, 1);
        Assert.False(reply.Truncated);
    }

    [Fact]
    public async Task Accepts_arguments_sent_as_a_json_string_and_survives_garbage()
    {
        var asString = ToolCallReply.Replace("""{"period":"thisMonth","limit":5}""", "\"{\\\"period\\\":\\\"today\\\"}\"");
        var (client, _) = Create((_, _) => Json(asString));
        var reply = await client.CompleteAsync([ChatMessage.User("q")], [TopProducts], CancellationToken.None);
        Assert.Equal("today", reply.Message.ToolCalls![0].Arguments.GetProperty("period").GetString());

        var garbage = ToolCallReply.Replace("""{"period":"thisMonth","limit":5}""", "\"not json\"");
        (client, _) = Create((_, _) => Json(garbage));
        reply = await client.CompleteAsync([ChatMessage.User("q")], [TopProducts], CancellationToken.None);
        Assert.Equal(JsonValueKind.Object, reply.Message.ToolCalls![0].Arguments.ValueKind);
        Assert.Empty(reply.Message.ToolCalls[0].Arguments.EnumerateObject());
    }

    [Fact]
    public async Task Sends_the_verified_request_shape()
    {
        var (client, handler) = Create((_, _) => Json(ToolCallReply),
            new OllamaOptions { Model = "qwen3.5:9b", Temperature = 0.2, ContextLength = 8192, Think = false, KeepAlive = "10m" });
        var args = JsonDocument.Parse("""{"period":"thisMonth"}""").RootElement;
        var conversation = new[]
        {
            ChatMessage.System("rules"),
            ChatMessage.User("Top five this month?"),
            new ChatMessage(ChatRole.Assistant, "", [new ToolCall("GetTopSellingProducts", args)]),
            ChatMessage.ToolResult("GetTopSellingProducts", """{"products":[]}""")
        };

        await client.CompleteAsync(conversation, [TopProducts], CancellationToken.None);

        var (path, body) = Assert.Single(handler.Requests);
        Assert.Equal("/api/chat", path);
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("qwen3.5:9b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal("10m", root.GetProperty("keep_alive").GetString());
        Assert.Equal(0.2, root.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());

        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["system", "user", "assistant", "tool"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("GetTopSellingProducts", messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("thisMonth", messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetProperty("period").GetString());
        Assert.Equal("GetTopSellingProducts", messages[3].GetProperty("tool_name").GetString());
        Assert.False(messages[1].TryGetProperty("tool_name", out _)); // nulls are omitted, not sent as null

        var tool = root.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("GetTopSellingProducts", tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("function").GetProperty("parameters").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Omits_tools_when_there_are_none_and_reports_truncation()
    {
        var (client, handler) = Create((_, _) => Json("""{"message":{"role":"assistant","content":"Hello"},"done_reason":"length","prompt_eval_count":10,"eval_count":5,"total_duration":1}"""));
        var reply = await client.CompleteAsync([ChatMessage.User("hi")], [], CancellationToken.None);

        Assert.Equal("Hello", reply.Message.Content);
        Assert.Null(reply.Message.ToolCalls);
        Assert.True(reply.Truncated);
        Assert.False(JsonDocument.Parse(handler.Requests[0].Body).RootElement.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task Missing_model_says_how_to_install_it()
    {
        var (client, _) = Create((_, _) => Json("""{"error":"model 'qwen3.5:9b' not found"}""", HttpStatusCode.NotFound));
        var ex = await Assert.ThrowsAsync<AIUnavailableException>(() => client.CompleteAsync([ChatMessage.User("q")], [], CancellationToken.None));
        Assert.Contains("ollama pull qwen3.5:9b", ex.Message);
    }

    [Fact]
    public async Task Server_down_and_server_error_become_friendly_unavailable_errors()
    {
        var (down, _) = Create((_, _) => throw new HttpRequestException("Connection refused"));
        var ex = await Assert.ThrowsAsync<AIUnavailableException>(() => down.CompleteAsync([ChatMessage.User("q")], [], CancellationToken.None));
        Assert.Contains("offline", ex.Message);

        var (broken, _) = Create((_, _) => Json("""{"error":"boom"}""", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<AIUnavailableException>(() => broken.CompleteAsync([ChatMessage.User("q")], [], CancellationToken.None));

        var (unreadable, _) = Create((_, _) => Json("<html>not json</html>"));
        await Assert.ThrowsAsync<AIUnavailableException>(() => unreadable.CompleteAsync([ChatMessage.User("q")], [], CancellationToken.None));
    }

    [Fact]
    public async Task A_timeout_is_unavailable_but_a_caller_cancel_is_a_cancel()
    {
        var (slow, _) = Create((_, _) => throw new TaskCanceledException("timeout"));
        var ex = await Assert.ThrowsAsync<AIUnavailableException>(() => slow.CompleteAsync([ChatMessage.User("q")], [], CancellationToken.None));
        Assert.Contains("too long", ex.Message);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (any, _) = Create((_, _) => Json(ToolCallReply));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => any.CompleteAsync([ChatMessage.User("q")], [], cts.Token));
    }

    [Fact]
    public async Task Status_reports_version_installation_and_tool_support()
    {
        var (client, _) = Create((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/version" => Json("""{"version":"0.40.13"}"""),
            "/api/show" => Json("""{"capabilities":["completion","vision","tools","thinking"]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var status = await client.GetStatusAsync(CancellationToken.None);
        Assert.True(status.Ready);
        Assert.Equal("0.40.13", status.ServerVersion);

        var (noTools, _) = Create((req, _) => req.RequestUri!.AbsolutePath == "/api/version"
            ? Json("""{"version":"0.40.13"}""") : Json("""{"capabilities":["embedding"]}"""));
        status = await noTools.GetStatusAsync(CancellationToken.None);
        Assert.False(status.Ready);
        Assert.Contains("tool calling", status.Problem);

        var (missing, _) = Create((req, _) => req.RequestUri!.AbsolutePath == "/api/version"
            ? Json("""{"version":"0.40.13"}""") : new HttpResponseMessage(HttpStatusCode.NotFound));
        status = await missing.GetStatusAsync(CancellationToken.None);
        Assert.False(status.ModelInstalled);

        var (down, _) = Create((_, _) => throw new HttpRequestException("refused"));
        status = await down.GetStatusAsync(CancellationToken.None);
        Assert.False(status.Reachable);
        Assert.Equal("Ollama is not running.", status.Problem);
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://192.168.1.20:11434", false)]
    [InlineData("https://ollama.example.com", false)]
    public void Only_a_local_ollama_address_is_allowed_by_default(string url, bool allowed) =>
        Assert.Equal(allowed, DependencyInjection.IsLoopback(url));

    [Theory]
    [InlineData("qwen3.5:9b", false)]
    [InlineData("glm-5.1:cloud", true)]
    [InlineData("gpt-oss:120b-cloud", true)]
    public void Cloud_models_are_recognised_and_refused(string model, bool cloud) =>
        Assert.Equal(cloud, DependencyInjection.IsCloudModel(model));
}
