namespace TriageBot.Core.Domain;

/// <summary>
/// Uniform result returned by every agent tool. Serialized back to the LLM, so <see cref="Message"/>
/// is written to be read by a model: clear, self-contained, and explicit about success or failure.
/// </summary>
public sealed record ToolResult(bool Success, string Message)
{
    public static ToolResult Ok(string message) => new(true, message);
    public static ToolResult Fail(string message) => new(false, message);
}
