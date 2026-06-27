using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Persistence;
using TriageBot.Infrastructure.Tools;
using Xunit;

namespace TriageBot.Tests;

public class TicketToolsTests
{
    private static TriageBotDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TriageBotDbContext>()
            .UseInMemoryDatabase($"tools-{Guid.NewGuid()}")
            .Options);

    /// <summary>Seeds one ticket + one run and returns (db, ticketId, runId) ready for tool calls.</summary>
    private static async Task<(TriageBotDbContext db, Guid ticketId, Guid runId)> ArrangeAsync()
    {
        var db = NewDb();
        var ticket = new Ticket
        {
            Subject = "VPN keeps disconnecting",
            Body = "The VPN drops every few minutes.",
            RequesterEmail = "user@contoso.com",
            Status = TicketStatus.New
        };
        var run = new AgentRun { TicketId = ticket.Id, Provider = "local" };
        db.Tickets.Add(ticket);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        return (db, ticket.Id, run.Id);
    }

    [Fact]
    public async Task RecordClassification_sets_category_and_urgency_and_logs_step()
    {
        var (db, ticketId, runId) = await ArrangeAsync();
        var tools = new TicketTools(db, runId);

        var result = await tools.RecordClassificationAsync(ticketId, TicketCategory.Network, TicketUrgency.Medium, "VPN keyword");

        Assert.True(result.Success);
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal(TicketCategory.Network, ticket!.Category);
        Assert.Equal(TicketUrgency.Medium, ticket.Urgency);
        Assert.NotNull(ticket.UpdatedAtUtc);

        var step = await db.AgentSteps.SingleAsync();
        Assert.Equal("record_classification", step.ToolName);
        Assert.Equal(0, step.StepIndex);
        Assert.Contains("Network", step.ArgumentsJson!);
        Assert.Contains("Medium", step.ArgumentsJson!);
    }

    [Fact]
    public async Task DraftReply_saves_draft_and_logs_step()
    {
        var (db, ticketId, runId) = await ArrangeAsync();
        var tools = new TicketTools(db, runId);

        var result = await tools.DraftReplyAsync(ticketId, "Hi, please reconnect your VPN and try again.");

        Assert.True(result.Success);
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal("Hi, please reconnect your VPN and try again.", ticket!.DraftReply);

        var step = await db.AgentSteps.SingleAsync();
        Assert.Equal("draft_reply", step.ToolName);
    }

    [Fact]
    public async Task SaveTicketResult_sets_status_and_completes_run()
    {
        var (db, ticketId, runId) = await ArrangeAsync();
        var tools = new TicketTools(db, runId);

        var result = await tools.SaveTicketResultAsync(ticketId, TicketStatus.Resolved);

        Assert.True(result.Success);
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal(TicketStatus.Resolved, ticket!.Status);

        var run = await db.AgentRuns.FindAsync(runId);
        Assert.NotNull(run!.CompletedAtUtc);
        Assert.Contains("Resolved", run.Outcome!);

        Assert.Equal("save_ticket_result", (await db.AgentSteps.SingleAsync()).ToolName);
    }

    [Fact]
    public async Task EscalateToHuman_sets_escalated_and_records_reason()
    {
        var (db, ticketId, runId) = await ArrangeAsync();
        var tools = new TicketTools(db, runId);

        var result = await tools.EscalateToHumanAsync(ticketId, "Critical production outage affecting all users.");

        Assert.True(result.Success);
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Equal(TicketStatus.Escalated, ticket!.Status);

        var run = await db.AgentRuns.FindAsync(runId);
        Assert.Contains("Critical production outage", run!.Outcome!);
    }

    [Fact]
    public async Task Unknown_ticket_returns_clear_failure_and_still_logs_step()
    {
        var (db, _, runId) = await ArrangeAsync();
        var tools = new TicketTools(db, runId);
        var missingId = Guid.NewGuid();

        var result = await tools.SaveTicketResultAsync(missingId, TicketStatus.Resolved);

        Assert.False(result.Success);
        Assert.Contains("was not found", result.Message);

        // The failed attempt is still audited as a step.
        var step = await db.AgentSteps.SingleAsync();
        Assert.Equal("save_ticket_result", step.ToolName);
        Assert.Contains("was not found", step.Message!);
    }

    [Fact]
    public async Task AsAITools_exposes_named_and_described_functions()
    {
        var (db, _, runId) = await ArrangeAsync();
        var tools = new TicketTools(db, runId);

        var functions = tools.AsAITools().OfType<AIFunction>().ToList();

        Assert.Equal(4, functions.Count);
        Assert.Contains("record_classification", functions.Select(f => f.Name));
        Assert.Contains("escalate_to_human", functions.Select(f => f.Name));
        // Descriptions (read by the LLM) flow from the [Description] attributes.
        Assert.All(functions, f => Assert.False(string.IsNullOrWhiteSpace(f.Description)));
    }

    [Fact]
    public async Task DraftReply_with_empty_text_fails_validation()
    {
        var (db, ticketId, runId) = await ArrangeAsync();
        var tools = new TicketTools(db, runId);

        var result = await tools.DraftReplyAsync(ticketId, "   ");

        Assert.False(result.Success);
        var ticket = await db.Tickets.FindAsync(ticketId);
        Assert.Null(ticket!.DraftReply);
    }
}
