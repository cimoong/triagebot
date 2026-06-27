using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Persistence;

namespace TriageBot.Infrastructure.Tools;

/// <summary>
/// Concrete agent tools that mutate the ticket and append an <see cref="AgentStep"/> per call.
/// Bound to a single <see cref="AgentRun"/> (passed in), so the run id is never exposed to the LLM —
/// the model only supplies domain arguments.
/// </summary>
public sealed class TicketTools : ITicketTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TriageBotDbContext _db;
    private readonly Guid _agentRunId;

    public TicketTools(TriageBotDbContext db, Guid agentRunId)
    {
        _db = db;
        _agentRunId = agentRunId;
    }

    [Description("Record the category and urgency you have decided for the ticket. Call this once you have classified it.")]
    public async Task<ToolResult> RecordClassificationAsync(
        [Description("The id of the ticket being classified.")] Guid ticketId,
        [Description("The functional category that best fits the ticket.")] TicketCategory category,
        [Description("How urgently the ticket needs attention.")] TicketUrgency urgency,
        [Description("A short justification for this classification, for the audit trail.")] string reasoning,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null)
            return await FailAsync("record_classification", new { ticketId, category, urgency, reasoning },
                $"Ticket {ticketId} was not found. Use a valid ticket id.", cancellationToken);

        ticket.Category = category;
        ticket.Urgency = urgency;
        ticket.UpdatedAtUtc = DateTime.UtcNow;

        return await SucceedAsync("record_classification", new { ticketId, category, urgency, reasoning },
            $"Recorded classification {category}/{urgency} for ticket {ticketId}.", cancellationToken);
    }

    [Description("Save the reply you have drafted for the ticket. The draft is held for human approval before it is sent.")]
    public async Task<ToolResult> DraftReplyAsync(
        [Description("The id of the ticket to attach the draft to.")] Guid ticketId,
        [Description("The full text of the reply to send to the requester.")] string draftText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftText))
            return await FailAsync("draft_reply", new { ticketId, draftText },
                "The draft reply text is empty. Provide the reply you want to save.", cancellationToken);

        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null)
            return await FailAsync("draft_reply", new { ticketId, draftText },
                $"Ticket {ticketId} was not found. Use a valid ticket id.", cancellationToken);

        ticket.DraftReply = draftText;
        ticket.UpdatedAtUtc = DateTime.UtcNow;

        return await SucceedAsync("draft_reply", new { ticketId, draftLength = draftText.Length },
            $"Saved a {draftText.Length}-character draft reply for ticket {ticketId}.", cancellationToken);
    }

    [Description("Finalize the ticket with a terminal status such as Resolved. This is a final action that requires prior human approval.")]
    public async Task<ToolResult> SaveTicketResultAsync(
        [Description("The id of the ticket to finalize.")] Guid ticketId,
        [Description("The terminal status to set, e.g. Resolved.")] TicketStatus status,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null)
            return await FailAsync("save_ticket_result", new { ticketId, status },
                $"Ticket {ticketId} was not found. Use a valid ticket id.", cancellationToken);

        ticket.Status = status;
        ticket.UpdatedAtUtc = DateTime.UtcNow;
        await CompleteRunAsync($"Ticket finalized with status {status}.", cancellationToken);

        return await SucceedAsync("save_ticket_result", new { ticketId, status },
            $"Ticket {ticketId} finalized with status {status}.", cancellationToken);
    }

    [Description("Escalate the ticket to a human agent, recording the reason. This is a final action that requires prior human approval.")]
    public async Task<ToolResult> EscalateToHumanAsync(
        [Description("The id of the ticket to escalate.")] Guid ticketId,
        [Description("Why this ticket needs a human, e.g. critical outage or out-of-policy request.")] string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return await FailAsync("escalate_to_human", new { ticketId, reason },
                "An escalation reason is required. Explain why a human is needed.", cancellationToken);

        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null)
            return await FailAsync("escalate_to_human", new { ticketId, reason },
                $"Ticket {ticketId} was not found. Use a valid ticket id.", cancellationToken);

        ticket.Status = TicketStatus.Escalated;
        ticket.UpdatedAtUtc = DateTime.UtcNow;
        await CompleteRunAsync($"Escalated to human: {reason}", cancellationToken);

        return await SucceedAsync("escalate_to_human", new { ticketId, reason },
            $"Ticket {ticketId} escalated to a human. Reason recorded.", cancellationToken);
    }

    /// <summary>
    /// Exposes the four tools as <see cref="AIFunction"/>s for an agent's <c>ChatOptions.Tools</c>.
    /// Names are passed explicitly so the function the model calls matches the logged <see cref="AgentStep.ToolName"/>;
    /// descriptions come from the <see cref="DescriptionAttribute"/> on each method (description: null below).
    /// </summary>
    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(RecordClassificationAsync, "record_classification", description: null),
        AIFunctionFactory.Create(DraftReplyAsync, "draft_reply", description: null),
        AIFunctionFactory.Create(SaveTicketResultAsync, "save_ticket_result", description: null),
        AIFunctionFactory.Create(EscalateToHumanAsync, "escalate_to_human", description: null)
    ];

    // --- shared logging / persistence helpers (used by every tool) ---

    private Task<ToolResult> SucceedAsync(string toolName, object arguments, string message, CancellationToken ct)
        => LogAndSaveAsync(toolName, arguments, ToolResult.Ok(message), ct);

    private Task<ToolResult> FailAsync(string toolName, object arguments, string message, CancellationToken ct)
        => LogAndSaveAsync(toolName, arguments, ToolResult.Fail(message), ct);

    /// <summary>Appends exactly one <see cref="AgentStep"/> for the call and persists all pending changes.</summary>
    private async Task<ToolResult> LogAndSaveAsync(string toolName, object arguments, ToolResult result, CancellationToken ct)
    {
        var stepIndex = await _db.AgentSteps.CountAsync(s => s.AgentRunId == _agentRunId, ct);

        _db.AgentSteps.Add(new AgentStep
        {
            AgentRunId = _agentRunId,
            StepIndex = stepIndex,
            ToolName = toolName,
            ArgumentsJson = JsonSerializer.Serialize(arguments, JsonOptions),
            ResultJson = JsonSerializer.Serialize(result, JsonOptions),
            Message = result.Message
        });

        await _db.SaveChangesAsync(ct);
        return result;
    }

    private async Task CompleteRunAsync(string outcome, CancellationToken ct)
    {
        var run = await _db.AgentRuns.FindAsync([_agentRunId], ct);
        if (run is null)
            return;

        run.Outcome = outcome;
        run.CompletedAtUtc = DateTime.UtcNow;
    }
}
