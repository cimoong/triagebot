using TriageBot.Core.Enums;

namespace TriageBot.Infrastructure.Ai;

/// <summary>Top-level AI settings (config section "Ai").</summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    /// <summary>Provider used when a session starts. Bound from "Ai:DefaultProvider".</summary>
    public AiProvider DefaultProvider { get; set; } = AiProvider.Local;
}

/// <summary>Local Ollama provider settings (config section "LocalAi"). No real secret — "ollama" is a placeholder.</summary>
public sealed class LocalAiOptions
{
    public const string Section = "LocalAi";

    public string Endpoint { get; set; } = "http://localhost:11434/v1";
    public string ApiKey { get; set; } = "ollama";
    public string ChatModel { get; set; } = "qwen3:8b";

    /// <summary>Generous request timeout; local CPU inference can be slow.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>Gemini provider settings (config section "Gemini"). ApiKey comes from user-secrets — never commit it.</summary>
public sealed class GeminiOptions
{
    public const string Section = "Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string ChatModel { get; set; } = "gemini-2.5-flash";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>Groq provider settings (config section "Groq"). ApiKey comes from user-secrets/env — never commit it.</summary>
public sealed class GroqOptions
{
    public const string Section = "Groq";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Main tool-calling model used for triage.</summary>
    public string ChatModel { get; set; } = "llama-3.3-70b-versatile";

    /// <summary>
    /// Optional lighter/cheaper model reserved for a future cost optimization (e.g. running
    /// classification on a small model). Not wired into the agent yet.
    /// </summary>
    public string ClassificationModel { get; set; } = "llama-3.1-8b-instant";

    public string Endpoint { get; set; } = "https://api.groq.com/openai/v1";

    public int TimeoutSeconds { get; set; } = 120;
}
