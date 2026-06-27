using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Agent;
using TriageBot.Infrastructure.Persistence;
using Xunit;

namespace TriageBot.Tests;

public class TicketApprovalServiceTests
{
    private static TriageBotDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TriageBotDbContext>()
            .UseInMemoryDatabase($"approval-{Guid.NewGuid()}")
            .Options);

    private static TicketApprovalService NewService(TriageBotDbContext db) =>
        new(db, NullLogger<TicketApprovalService>.Instance);

    /// <summary>Seeds a ticket paused at AwaitingApproval with a run holding the given pending action.</summary>
    private static async Task<(TriageBotDbContext db, Guid ticketId)> ArrangePendingAsync(
        string pendingTool, string pendingArgsJson)
    {
        var db = NewDb();
        var ticket = new Ticket
        {
            Subject = "Test",
            Body = "Body",
            RequesterEmail = "user@contoso.com",
            Status = TicketStatus.AwaitingApproval,
            Category = TicketCategory.Software,
            Urgency = TicketUrgency.Medium,
            DraftReply = "Original draft."
        };
        var run = new AgentRun
        {
            TicketId = ticket.Id,
            Provider = "local",
            PendingToolName = pendingTool,
            PendingArgumentsJson = pendingArgsJson
        };
        db.Tickets.Add(ticket);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        return (db, ticket.Id);
    }

    [Fact]
    public async Task Approve_executes_pending_save_and_resolves()
    {
        var (db, ticketId) = await ArrangePendingAsync("save_ticket_result", """{"status":"Resolved"}""");
        var sut = NewService(db);

        var result = await sut.ApproveAsync(ticketId);

        Assert.Equal("Resolved", result!.Status);
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal(TicketStatus.Resolved, ticket!.Status);
        var run = await db.AgentRuns.FirstAsync(r => r.TicketId == ticketId);
        Assert.Null(run.PendingToolName); // cleared
        Assert.NotNull(run.CompletedAtUtc);
    }

    [Fact]
    public async Task Approve_executes_pending_escalation()
    {
        var (db, ticketId) = await ArrangePendingAsync("escalate_to_human", """{"reason":"Critical outage"}""");
        var sut = NewService(db);

        var result = await sut.ApproveAsync(ticketId);

        Assert.Equal("Escalated", result!.Status);
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal(TicketStatus.Escalated, ticket!.Status);
    }

    [Fact]
    public async Task Approve_with_edited_draft_saves_edit_before_finalizing()
    {
        var (db, ticketId) = await ArrangePendingAsync("save_ticket_result", """{"status":"Resolved"}""");
        var sut = NewService(db);

        await sut.ApproveAsync(ticketId, editedDraft: "Edited reply approved by human.");

        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal("Edited reply approved by human.", ticket!.DraftReply);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
    }

    [Fact]
    public async Task Reject_does_not_execute_final_action_and_marks_rejected()
    {
        var (db, ticketId) = await ArrangePendingAsync("save_ticket_result", """{"status":"Resolved"}""");
        var sut = NewService(db);

        var result = await sut.RejectAsync(ticketId, "Draft is inaccurate.");

        Assert.Equal("Rejected", result!.Status);
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal(TicketStatus.Rejected, ticket!.Status); // NOT Resolved
        var run = await db.AgentRuns.FirstAsync(r => r.TicketId == ticketId);
        Assert.Null(run.PendingToolName);
    }

    [Fact]
    public async Task Approving_twice_is_idempotent()
    {
        var (db, ticketId) = await ArrangePendingAsync("save_ticket_result", """{"status":"Resolved"}""");
        var sut = NewService(db);

        await sut.ApproveAsync(ticketId);
        var second = await sut.ApproveAsync(ticketId); // already Resolved

        Assert.Equal("Resolved", second!.Status);
        Assert.Contains("already", second.Message, StringComparison.OrdinalIgnoreCase);
        // Exactly one terminal run, still resolved.
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal(TicketStatus.Resolved, ticket!.Status);
    }

    [Fact]
    public async Task Approve_unknown_ticket_returns_null()
    {
        var db = NewDb();
        var sut = NewService(db);

        Assert.Null(await sut.ApproveAsync(Guid.NewGuid()));
    }
}
