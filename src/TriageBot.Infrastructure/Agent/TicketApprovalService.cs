using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Persistence;
using TriageBot.Infrastructure.Tools;

namespace TriageBot.Infrastructure.Agent;

/// <summary>
/// Resolves the human approval gate. On approve it deterministically executes the final action the agent
/// left pending (no LLM re-run); on reject it cancels it. Idempotent: once a ticket is no longer
/// <see cref="TicketStatus.AwaitingApproval"/>, approve/reject become safe no-ops.
/// </summary>
public sealed class TicketApprovalService : ITicketApprovalService
{
    private readonly TriageBotDbContext _db;
    private readonly ILogger<TicketApprovalService> _logger;

    public TicketApprovalService(TriageBotDbContext db, ILogger<TicketApprovalService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApprovalResult?> ApproveAsync(Guid ticketId, string? editedDraft = null, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null)
            return null;

        if (ticket.Status != TicketStatus.AwaitingApproval)
        {
            _logger.LogInformation("Approve ignored for ticket {TicketId}: status is {Status}, not AwaitingApproval.", ticketId, ticket.Status);
            return new ApprovalResult(ticketId, ticket.Status.ToString(), $"No pending action; ticket is already {ticket.Status}.");
        }

        var run = await LatestPendingRunAsync(ticketId, cancellationToken);
        if (run is null)
        {
            _logger.LogWarning("Ticket {TicketId} is AwaitingApproval but has no pending run; resetting to Processing.", ticketId);
            ticket.Status = TicketStatus.Processing;
            await _db.SaveChangesAsync(cancellationToken);
            return new ApprovalResult(ticketId, ticket.Status.ToString(), "No pending action was found; ticket returned to Processing.");
        }

        var tools = new TicketTools(_db, run.Id);

        // Optional human edit of the draft before the final action is taken.
        if (!string.IsNullOrWhiteSpace(editedDraft))
            await tools.DraftReplyAsync(ticketId, editedDraft, cancellationToken);

        await tools.LogStepAsync("approval_granted",
            new { tool = run.PendingToolName, draftEdited = !string.IsNullOrWhiteSpace(editedDraft) },
            $"Human approved '{run.PendingToolName}'.", cancellationToken);

        // Execute the exact action the agent proposed.
        var toolName = run.PendingToolName;
        switch (toolName)
        {
            case TicketTools.SaveTicketResultTool:
                var status = ParseEnumArg(run.PendingArgumentsJson, "status", TicketStatus.Resolved);
                await tools.SaveTicketResultAsync(ticketId, status, cancellationToken);
                break;

            case TicketTools.EscalateToHumanTool:
                var reason = ParseStringArg(run.PendingArgumentsJson, "reason") ?? "Escalation approved by a human reviewer.";
                await tools.EscalateToHumanAsync(ticketId, reason, cancellationToken);
                break;

            default:
                _logger.LogError("Ticket {TicketId} had an unknown pending tool '{Tool}'.", ticketId, toolName);
                return new ApprovalResult(ticketId, ticket.Status.ToString(), $"Unknown pending action '{toolName}'.");
        }

        // Clear the pending action so a second approval is a no-op.
        run.PendingToolName = null;
        run.PendingArgumentsJson = null;
        await _db.SaveChangesAsync(cancellationToken);

        await _db.Entry(ticket).ReloadAsync(cancellationToken);
        _logger.LogInformation("Ticket {TicketId} approved: executed '{Tool}', status now {Status}.", ticketId, toolName, ticket.Status);
        return new ApprovalResult(ticketId, ticket.Status.ToString(), $"Approved. Executed '{toolName}'.");
    }

    public async Task<ApprovalResult?> RejectAsync(Guid ticketId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null)
            return null;

        if (ticket.Status != TicketStatus.AwaitingApproval)
        {
            _logger.LogInformation("Reject ignored for ticket {TicketId}: status is {Status}, not AwaitingApproval.", ticketId, ticket.Status);
            return new ApprovalResult(ticketId, ticket.Status.ToString(), $"No pending action; ticket is already {ticket.Status}.");
        }

        var run = await LatestPendingRunAsync(ticketId, cancellationToken);
        if (run is not null)
        {
            var tools = new TicketTools(_db, run.Id);
            await tools.LogStepAsync("approval_rejected",
                new { tool = run.PendingToolName, reason },
                $"Human rejected '{run.PendingToolName}'." + (reason is null ? "" : $" Reason: {reason}"), cancellationToken);

            run.Outcome = $"Rejected by human: {reason ?? "no reason given"}.";
            run.CompletedAtUtc = DateTime.UtcNow;
            run.PendingToolName = null;
            run.PendingArgumentsJson = null;
        }

        // The pending final action is NOT executed.
        ticket.Status = TicketStatus.Rejected;
        ticket.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ticket {TicketId} rejected; final action cancelled.", ticketId);
        return new ApprovalResult(ticketId, ticket.Status.ToString(), "Rejected. The proposed final action was not executed.");
    }

    private Task<AgentRun?> LatestPendingRunAsync(Guid ticketId, CancellationToken ct) =>
        _db.AgentRuns
            .Where(r => r.TicketId == ticketId && r.PendingToolName != null)
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

    private static string? ParseStringArg(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(key, out var value))
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();

        return null;
    }

    private static TEnum ParseEnumArg<TEnum>(string? json, string key, TEnum fallback) where TEnum : struct, Enum
    {
        var raw = ParseStringArg(json, key);
        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) ? parsed : fallback;
    }
}
