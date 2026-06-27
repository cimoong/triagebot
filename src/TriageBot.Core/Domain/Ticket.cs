using TriageBot.Core.Enums;

namespace TriageBot.Core.Domain;

/// <summary>
/// A support ticket as it flows through the triage workflow:
/// read -> classify -> draft reply -> human approval -> save/escalate.
/// </summary>
public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string RequesterEmail { get; set; } = string.Empty;

    /// <summary>Null until the agent has classified the ticket.</summary>
    public TicketCategory? Category { get; set; }

    /// <summary>Null until the agent has assessed urgency.</summary>
    public TicketUrgency? Urgency { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.New;

    /// <summary>Agent-generated draft reply, awaiting human approval before it is sent.</summary>
    public string? DraftReply { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Agent executions that have processed this ticket (most recent triage attempts).</summary>
    public ICollection<AgentRun> AgentRuns { get; set; } = new List<AgentRun>();
}
