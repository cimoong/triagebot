using TriageBot.Core.Enums;

namespace TriageBot.Web.Components;

/// <summary>Maps domain enums to Bootstrap badge classes for consistent colour coding across the UI.</summary>
public static class UiBadges
{
    public static string Urgency(TicketUrgency? urgency) => urgency switch
    {
        TicketUrgency.Low => "bg-secondary",
        TicketUrgency.Medium => "bg-info text-dark",
        TicketUrgency.High => "bg-warning text-dark",
        TicketUrgency.Critical => "bg-danger",
        _ => "bg-light text-dark"
    };

    public static string Status(TicketStatus status) => status switch
    {
        TicketStatus.New => "bg-secondary",
        TicketStatus.Processing => "bg-info text-dark",
        TicketStatus.AwaitingApproval => "bg-warning text-dark",
        TicketStatus.Resolved => "bg-success",
        TicketStatus.Escalated => "bg-primary",
        TicketStatus.Rejected => "bg-danger",
        _ => "bg-light text-dark"
    };

    public static string Category(TicketCategory? category) =>
        category is null ? "bg-light text-dark" : "bg-secondary";
}
