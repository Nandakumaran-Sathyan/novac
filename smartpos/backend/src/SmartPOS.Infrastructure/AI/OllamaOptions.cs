using System.ComponentModel.DataAnnotations;

namespace SmartPOS.Infrastructure.AI;

/// <summary>Bound from the "Ollama" configuration section.</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Where the Ollama server listens. Only the backend talks to it; the browser never does.</summary>
    [Required]
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>The chat model tag, exactly as <c>ollama list</c> shows it.</summary>
    [Required]
    public string Model { get; set; } = "qwen3.5:9b";

    /// <summary>
    /// Off by default: shop data must stay on this machine, so the backend refuses a non-loopback Ollama address
    /// unless this is deliberately switched on (for a future per-store agent reached over a private network).
    /// </summary>
    public bool AllowRemoteHost { get; set; }

    /// <summary>How long one model call may take before the request is abandoned. Includes loading the model.</summary>
    [Range(5, 600)]
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Low by default: tool selection must be predictable, not creative (the model's own default is 1.0).</summary>
    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.2;

    /// <summary>Context window in tokens. Bigger fits longer conversations but costs memory (about 0.5 GB per 8K on this model).</summary>
    [Range(2048, 131072)]
    public int ContextLength { get; set; } = 8192;

    /// <summary>
    /// Qwen 3.5 "thinking" mode. Measured on this machine it made a tool-call reply about 45% slower; the
    /// reasoning text is never shown to users. Kept configurable because it may help with multi-part questions.
    /// </summary>
    public bool Think { get; set; }

    /// <summary>How long Ollama keeps the model in memory after a call (Ollama duration syntax, e.g. "10m", "-1" = forever).</summary>
    [Required]
    public string KeepAlive { get; set; } = "10m";
}
