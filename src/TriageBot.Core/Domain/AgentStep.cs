namespace TriageBot.Core.Domain;

/// <summary>
/// One step in an <see cref="AgentRun"/>: either a tool invocation (with JSON arguments/result)
/// or a piece of reasoning/text. Kept append-only so the run can be replayed and audited.
/// </summary>
public class AgentStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentRunId { get; set; }

    public AgentRun? AgentRun { get; set; }

    /// <summary>Zero-based position of this step within its run.</summary>
    public int StepIndex { get; set; }

    /// <summary>Name of the tool invoked, or null for a pure reasoning/text step.</summary>
    public string? ToolName { get; set; }

    /// <summary>JSON-serialized arguments passed to the tool, if any.</summary>
    public string? ArgumentsJson { get; set; }

    /// <summary>JSON-serialized result returned by the tool, if any.</summary>
    public string? ResultJson { get; set; }

    /// <summary>Free-text reasoning or model output for this step.</summary>
    public string? Message { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
