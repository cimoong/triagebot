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

    public TicketCategory Category { get; set; } = TicketCategory.Unknown;

    public TicketPriority Priority { get; set; } = TicketPriority.Low;

    public TicketStatus Status { get; set; } = TicketStatus.New;

    /// <summary>Agent-generated draft reply, awaiting human approval before it is sent.</summary>
    public string? DraftReply { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
