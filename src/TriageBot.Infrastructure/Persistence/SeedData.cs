using TriageBot.Core.Domain;
using TriageBot.Core.Enums;

namespace TriageBot.Infrastructure.Persistence;

/// <summary>
/// Deterministic sample tickets seeded via EF Core <c>HasData</c>. Fixed Ids and timestamps keep
/// the seed idempotent: re-running migrations does not create duplicates or churn the schema.
/// </summary>
internal static class SeedData
{
    // A fixed instant so HasData stays stable across migrations (no DateTime.UtcNow at model-build time).
    private static readonly DateTime SeededAtUtc = new(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<Ticket> Tickets { get; } =
    [
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111101"),
            Subject = "Cannot login to portal",
            Body = "I keep getting 'invalid credentials' on the staff portal even though my password is correct.",
            RequesterEmail = "dewi@contoso.com",
            Category = TicketCategory.AccountAccess,
            Urgency = TicketUrgency.High,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111102"),
            Subject = "VPN keeps disconnecting",
            Body = "The corporate VPN drops every few minutes when I work from home, making it hard to stay connected.",
            RequesterEmail = "budi@contoso.com",
            Category = TicketCategory.Network,
            Urgency = TicketUrgency.Medium,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111103"),
            Subject = "Request: install Visual Studio",
            Body = "Please install Visual Studio 2022 Professional on my workstation for the new project.",
            RequesterEmail = "arif@contoso.com",
            Category = TicketCategory.Software,
            Urgency = TicketUrgency.Low,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111104"),
            Subject = "Printer offline 3rd floor",
            Body = "The shared printer near the 3rd floor kitchen shows as offline and nobody can print.",
            RequesterEmail = "siti@contoso.com",
            Category = TicketCategory.Hardware,
            Urgency = TicketUrgency.Medium,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111105"),
            Subject = "URGENT: production app down for all users",
            Body = "The order-management application is returning 500 errors for everyone. This is a full outage affecting production.",
            RequesterEmail = "ops@contoso.com",
            Category = TicketCategory.Software,
            Urgency = TicketUrgency.Critical,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111106"),
            Subject = "Forgot password",
            Body = "I forgot my Windows password and cannot sign in to my laptop. Please help me reset it.",
            RequesterEmail = "rina@contoso.com",
            Category = TicketCategory.AccountAccess,
            Urgency = TicketUrgency.Medium,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111107"),
            Subject = "Email not syncing on phone",
            Body = "My Outlook mailbox stopped syncing on my iPhone since yesterday; new messages only appear on the desktop.",
            RequesterEmail = "tono@contoso.com",
            Category = TicketCategory.Email,
            Urgency = TicketUrgency.Low,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111108"),
            Subject = "New laptop request",
            Body = "Onboarding a new analyst next week; please provision a standard laptop and accessories.",
            RequesterEmail = "hr@contoso.com",
            Category = TicketCategory.Hardware,
            Urgency = TicketUrgency.Low,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111109"),
            Subject = "Suspected data breach in finance share",
            Body = "Several files on the finance share were renamed with a strange extension and a ransom note appeared. Possible ransomware.",
            RequesterEmail = "finance@contoso.com",
            Category = TicketCategory.Other,
            Urgency = TicketUrgency.Critical,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111110"),
            Subject = "Wi-Fi down across 2nd floor",
            Body = "Wireless access points on the 2nd floor are unreachable; about 20 people have no connectivity.",
            RequesterEmail = "facilities@contoso.com",
            Category = TicketCategory.Network,
            Urgency = TicketUrgency.High,
            Status = TicketStatus.New,
            CreatedAtUtc = SeededAtUtc,
        },
    ];
}
