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

    /// <summary>Tool names that perform a final, impactful action and therefore require human approval.</summary>
    public const string SaveTicketResultTool = "save_ticket_result";
    public const string EscalateToHumanTool = "escalate_to_human";

    [Description("Propose finalizing the ticket with a terminal status such as Resolved. This does NOT take effect immediately: it is queued for human approval. After calling this, stop and wait for the human decision.")]
    public Task<ToolResult> RequestSaveTicketResultAsync(
        [Description("The id of the ticket to finalize.")] Guid ticketId,
        [Description("The terminal status to propose, e.g. Resolved.")] TicketStatus status,
        CancellationToken cancellationToken = default)
        => QueueForApprovalAsync(ticketId, SaveTicketResultTool, new { status }, cancellationToken);

    [Description("Propose escalating the ticket to a human agent, with a reason. This does NOT escalate immediately: it is queued for human approval. After calling this, stop and wait for the human decision.")]
    public Task<ToolResult> RequestEscalateToHumanAsync(
        [Description("The id of the ticket to escalate.")] Guid ticketId,
        [Description("Why this ticket needs a human, e.g. critical outage or out-of-policy request.")] string reason,
        CancellationToken cancellationToken = default)
        => QueueForApprovalAsync(ticketId, EscalateToHumanTool, new { reason }, cancellationToken);

    /// <summary>
    /// Exposes the four tools as <see cref="AIFunction"/>s for an agent's <c>ChatOptions.Tools</c>.
    /// Non-final tools run automatically; the two final actions are the "Request" variants, which queue the
    /// proposed action for human approval instead of executing it. Names are passed explicitly so the function
    /// the model calls matches the logged <see cref="AgentStep.ToolName"/>; descriptions come from the
    /// <see cref="DescriptionAttribute"/> on each method (description: null below).
    /// </summary>
    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(RecordClassificationAsync, "record_classification", description: null),
        AIFunctionFactory.Create(DraftReplyAsync, "draft_reply", description: null),
        AIFunctionFactory.Create(RequestSaveTicketResultAsync, SaveTicketResultTool, description: null),
        AIFunctionFactory.Create(RequestEscalateToHumanAsync, EscalateToHumanTool, description: null)
    ];

    /// <summary>
    /// Human-in-the-loop gate: instead of running a final action, persist it on the run as a pending
    /// proposal and move the ticket to <see cref="TicketStatus.AwaitingApproval"/>. The approval service
    /// later executes (or cancels) it. Idempotent within a run — a second proposal is ignored.
    /// </summary>
    private async Task<ToolResult> QueueForApprovalAsync(Guid ticketId, string toolName, object arguments, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FindAsync([ticketId], ct);
        if (ticket is null)
            return await FailAsync(toolName, arguments, $"Ticket {ticketId} was not found. Use a valid ticket id.", ct);

        var run = await _db.AgentRuns.FindAsync([_agentRunId], ct);
        if (run is { PendingToolName: not null })
            return ToolResult.Ok($"A '{run.PendingToolName}' action is already awaiting human approval. Stop and wait.");

        if (run is not null)
        {
            run.PendingToolName = toolName;
            run.PendingArgumentsJson = JsonSerializer.Serialize(arguments, JsonOptions);
        }

        ticket.Status = TicketStatus.AwaitingApproval;
        ticket.UpdatedAtUtc = DateTime.UtcNow;

        return await LogAndSaveAsync("awaiting_approval", new { tool = toolName, arguments },
            ToolResult.Ok($"Proposed '{toolName}' has been queued for human approval. Stop now and wait for the decision."), ct);
    }

    /// <summary>
    /// Appends a non-tool audit step (reasoning, awaiting-approval marker, approval decision) to this run.
    /// Used by the orchestration/approval services so every event in a run shows up in <see cref="AgentStep"/>.
    /// </summary>
    public Task LogStepAsync(string? toolName, object? arguments, string message, CancellationToken cancellationToken = default)
        => LogAndSaveAsync(toolName, arguments ?? new { }, ToolResult.Ok(message), cancellationToken);

    // --- shared logging / persistence helpers (used by every tool) ---

    private Task<ToolResult> SucceedAsync(string toolName, object arguments, string message, CancellationToken ct)
        => LogAndSaveAsync(toolName, arguments, ToolResult.Ok(message), ct);

    private Task<ToolResult> FailAsync(string toolName, object arguments, string message, CancellationToken ct)
        => LogAndSaveAsync(toolName, arguments, ToolResult.Fail(message), ct);

    /// <summary>Appends exactly one <see cref="AgentStep"/> for the call and persists all pending changes.</summary>
    private async Task<ToolResult> LogAndSaveAsync(string? toolName, object arguments, ToolResult result, CancellationToken ct)
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
