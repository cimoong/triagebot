using TriageBot.Core.Domain;
using TriageBot.Core.Enums;

namespace TriageBot.Core.Abstractions;

/// <summary>
/// The structured actions the triage agent can take on a ticket. The agent does the *reasoning*
/// (what category, what reply, whether to escalate); these tools *persist* those decisions
/// deterministically and append an audit step for each call.
/// </summary>
public interface ITicketTools
{
    /// <summary>Persist the agent's classification decision (category + urgency) onto the ticket.</summary>
    Task<ToolResult> RecordClassificationAsync(
        Guid ticketId, TicketCategory category, TicketUrgency urgency, string reasoning, CancellationToken cancellationToken = default);

    /// <summary>Persist the agent's drafted reply text onto the ticket, pending human approval.</summary>
    Task<ToolResult> DraftReplyAsync(
        Guid ticketId, string draftText, CancellationToken cancellationToken = default);

    /// <summary>Finalize the ticket with a terminal status (e.g. Resolved). Final action — gated by human approval.</summary>
    Task<ToolResult> SaveTicketResultAsync(
        Guid ticketId, TicketStatus status, CancellationToken cancellationToken = default);

    /// <summary>Escalate the ticket to a human and record why. Final action — gated by human approval.</summary>
    Task<ToolResult> EscalateToHumanAsync(
        Guid ticketId, string reason, CancellationToken cancellationToken = default);
}
