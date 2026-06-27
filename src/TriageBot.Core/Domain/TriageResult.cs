using TriageBot.Core.Enums;

namespace TriageBot.Core.Domain;

/// <summary>
/// The classification + suggested action produced by the triage agent for a single ticket.
/// This is a proposal: nothing is applied until a human approves it.
/// </summary>
public sealed record TriageResult(
    TicketCategory Category,
    TicketPriority Priority,
    string DraftReply,
    bool ShouldEscalate,
    string Reasoning);
