namespace TriageBot.Core.Enums;

/// <summary>Functional area a ticket belongs to. Drives routing and escalation rules.</summary>
public enum TicketCategory
{
    AccountAccess,
    Network,
    Software,
    Hardware,
    Email,
    Other
}

/// <summary>How urgently a ticket needs attention. Set by the agent, may be overridden by a human.</summary>
public enum TicketUrgency
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>Lifecycle of a ticket as it moves through the triage workflow.</summary>
public enum TicketStatus
{
    New,
    Processing,
    AwaitingApproval,
    Resolved,
    Escalated,
    Rejected
}
