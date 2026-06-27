namespace TriageBot.Core.Enums;

/// <summary>Functional area a ticket belongs to. Drives routing and escalation rules.</summary>
public enum TicketCategory
{
    Unknown = 0,
    Hardware,
    Software,
    Network,
    Account,
    Security,
    Other
}

/// <summary>Business urgency of a ticket. Set by the agent, may be overridden by a human.</summary>
public enum TicketPriority
{
    Low = 0,
    Medium,
    High,
    Critical
}

/// <summary>Lifecycle of a ticket as it moves through the triage workflow.</summary>
public enum TicketStatus
{
    New = 0,
    Triaged,
    AwaitingApproval,
    Replied,
    Escalated,
    Closed
}

/// <summary>Outcome of the human approval gate before any final action is taken.</summary>
public enum ApprovalDecision
{
    Pending = 0,
    Approved,
    Rejected
}
