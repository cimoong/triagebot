namespace TriageBot.Core.Enums;

/// <summary>Which LLM backend serves chat requests. Switchable at runtime per session.</summary>
public enum AiProvider
{
    /// <summary>Local Ollama instance (OpenAI-compatible API). Default — no API key required.</summary>
    Local,

    /// <summary>Google Gemini via its OpenAI-compatible endpoint. Requires an API key.</summary>
    Gemini,

    /// <summary>Groq via its OpenAI-compatible endpoint. Fast cloud inference. Requires an API key.</summary>
    Groq
}
