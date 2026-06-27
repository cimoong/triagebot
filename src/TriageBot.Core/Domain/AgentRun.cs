namespace TriageBot.Core.Domain;

/// <summary>
/// A single execution of the triage agent against one <see cref="Ticket"/>.
/// Captures which provider ran and a short outcome summary; the detailed trace lives in <see cref="AgentStep"/>s.
/// </summary>
public class AgentRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Which agent backend produced this run, e.g. "local" or "gemini".</summary>
    public string Provider { get; set; } = "local";

    /// <summary>Short human-readable summary of how the run ended.</summary>
    public string? Outcome { get; set; }

    /// <summary>Ordered trace of tool calls and reasoning for this run.</summary>
    public ICollection<AgentStep> Steps { get; set; } = new List<AgentStep>();
}
