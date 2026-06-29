using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Ai;
using TriageBot.Infrastructure.Persistence;
using TriageBot.Infrastructure.Tools;

namespace TriageBot.Infrastructure.Agent;

/// <summary>
/// Orchestrates a triage pass: opens an <see cref="AgentRun"/>, runs <see cref="TicketTriageAgent"/> with
/// tools bound to that run (so each tool call self-logs an <see cref="AgentStep"/>), records the agent's
/// final reasoning, and finalizes the run's provider/outcome.
/// </summary>
public sealed class TicketTriageService : ITicketTriageService
{
    private readonly TriageBotDbContext _db;
    private readonly TicketTriageAgent _agent;
    private readonly IAiClientResolver _resolver;
    private readonly ILogger<TicketTriageService> _logger;

    public TicketTriageService(
        TriageBotDbContext db,
        TicketTriageAgent agent,
        IAiClientResolver resolver,
        ILogger<TicketTriageService> logger)
    {
        _db = db;
        _agent = agent;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<TriageRunResult?> ProcessTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null)
        {
            _logger.LogWarning("ProcessTicket requested for unknown ticket {TicketId}.", ticketId);
            return null;
        }

        var provider = _resolver.ActiveProvider;

        // 1. Open the run up-front so the tools (bound to its id) can append their steps as they execute.
        var run = new AgentRun
        {
            TicketId = ticketId,
            Provider = provider.ToString().ToLowerInvariant(),
            StartedAtUtc = DateTime.UtcNow
        };
        _db.AgentRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        if (ticket.Status == Core.Enums.TicketStatus.New)
        {
            ticket.Status = Core.Enums.TicketStatus.Processing;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Triage run {RunId} started for ticket {TicketId} ({Provider}).", run.Id, ticketId, run.Provider);

        // 2. Run the agent. Non-final tools execute (and self-log). A final-action tool does NOT execute:
        //    it queues a pending proposal on the run and moves the ticket to AwaitingApproval, then the agent stops.
        var tools = new TicketTools(_db, run.Id);
        AgentResponse response;
        try
        {
            response = await _agent.RunAsync(ticket, provider, tools.AsAITools(), cancellationToken);
        }
        catch (Exception ex)
        {
            // The provider is unreachable/timed out, or a tool threw unexpectedly. Fail the run cleanly:
            // record it, leave a visible audit step, and don't strand the ticket in Processing.
            await FailRunAsync(run, ticket, tools, ex, cancellationToken);
            throw new TriageRunException(
                $"The triage agent could not complete. Is the '{provider}' AI provider running and reachable?", ex);
        }

        var awaitingApproval = run.PendingToolName is not null; // set by the final-action tool during the run
        if (awaitingApproval)
        {
            // 3a. HUMAN-IN-THE-LOOP: paused with the proposed final action persisted, awaiting a decision.
            run.Outcome = $"Awaiting human approval for '{run.PendingToolName}'.";
            // CompletedAtUtc stays null: the run is paused, not finished.
            _logger.LogInformation(
                "Triage run {RunId} for ticket {TicketId} PAUSED — awaiting approval for '{Tool}'.",
                run.Id, ticketId, run.PendingToolName);
        }
        else
        {
            // 3b. Agent finished without proposing a final action (e.g. asked the requester for clarification).
            await AppendReasoningStepAsync(run.Id, response.Text, cancellationToken);
            run.Outcome ??= Truncate(response.Text, 1000);
            run.CompletedAtUtc ??= DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var stepCount = await LogRunStepsAsync(run.Id, cancellationToken);
        _logger.LogInformation("Triage run {RunId} for ticket {TicketId}: {StepCount} steps. Outcome: {Outcome}",
            run.Id, ticketId, stepCount, run.Outcome);

        return new TriageRunResult(
            run.Id, ticketId, run.Provider, run.Outcome, stepCount, awaitingApproval, run.PendingToolName);
    }

    private async Task<int> LogRunStepsAsync(Guid runId, CancellationToken ct)
    {
        var steps = await _db.AgentSteps
            .Where(s => s.AgentRunId == runId)
            .OrderBy(s => s.StepIndex)
            .ToListAsync(ct);

        foreach (var step in steps)
        {
            _logger.LogInformation("Run {RunId} step {Index}: {Tool} -> {Message}",
                runId, step.StepIndex, step.ToolName ?? "(reasoning)", step.Message);
        }

        return steps.Count;
    }

    private async Task AppendReasoningStepAsync(Guid runId, string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var stepIndex = await _db.AgentSteps.CountAsync(s => s.AgentRunId == runId, ct);
        _db.AgentSteps.Add(new AgentStep
        {
            AgentRunId = runId,
            StepIndex = stepIndex,
            ToolName = null, // pure reasoning step
            Message = Truncate(text, 4000)
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records a failed run without throwing further: appends an error <see cref="AgentStep"/>, marks the run
    /// completed with a failure outcome, and rolls a still-Processing ticket back to New so it can be retried.
    /// </summary>
    private async Task FailRunAsync(AgentRun run, Ticket ticket, TicketTools tools, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Triage run {RunId} for ticket {TicketId} failed: {Message}", run.Id, ticket.Id, ex.Message);

        try
        {
            await tools.LogStepAsync("run_failed", new { error = ex.Message },
                $"The triage run failed: {ex.Message}", ct);

            run.Outcome = Truncate($"Failed: {ex.Message}", 1000);
            run.CompletedAtUtc = DateTime.UtcNow;

            // Don't leave the ticket stuck in Processing; allow another attempt.
            if (ticket.Status == Core.Enums.TicketStatus.Processing)
            {
                ticket.Status = Core.Enums.TicketStatus.New;
                ticket.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception persistEx)
        {
            // Persisting the failure must never mask the original error.
            _logger.LogError(persistEx, "Could not persist the failure of run {RunId}.", run.Id);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

/// <summary>Raised when a triage run cannot complete (e.g. the AI provider is unreachable). Carries a user-safe message.</summary>
public sealed class TriageRunException : Exception
{
    public TriageRunException(string message, Exception innerException) : base(message, innerException) { }
}
