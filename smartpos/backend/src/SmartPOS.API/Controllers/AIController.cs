using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartPOS.API.Infrastructure;
using SmartPOS.Application.AI;

namespace SmartPOS.API.Controllers;

/// <summary>The AI assistant. Requires a signed-in user like every other endpoint; the shop comes from the token.</summary>
[ApiController]
[Route("api/ai")]
[Produces("application/json")]
public sealed class AIController(IChatModelClient model, IAIService assistant) : ControllerBase
{
    /// <summary>
    /// Ask the assistant a question about your shop. Send earlier turns in <c>history</c> (user and assistant text
    /// only) for follow-up questions. 429 if you ask too fast or your previous question is still running; 503 if
    /// the AI model is unavailable.
    /// </summary>
    [HttpPost("chat")]
    [EnableRateLimiting(ApiServiceCollectionExtensions.AIRateLimitPolicy)]
    [ProducesResponseType<AIChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<AIChatResponse> Chat(AIChatRequest request, CancellationToken cancellationToken) =>
        assistant.ChatAsync(request, cancellationToken);

    /// <summary>Whether the assistant can answer right now: Ollama reachable, model installed, tool calling supported.</summary>
    [HttpGet("status")]
    [ProducesResponseType<ChatModelStatus>(StatusCodes.Status200OK)]
    public Task<ChatModelStatus> Status(CancellationToken cancellationToken) => model.GetStatusAsync(cancellationToken);
}
