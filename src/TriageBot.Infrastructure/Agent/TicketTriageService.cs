using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;
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

        // 2. Run the agent. Tools persist their own results + audit steps during the run.
        var tools = new TicketTools(_db, run.Id);
        var response = await _agent.RunAsync(ticket, provider, tools.AsAITools(), cancellationToken);

        // 3. Record the agent's final reasoning/summary as a step for the audit trail.
        await AppendReasoningStepAsync(run.Id, response.Text, cancellationToken);

        // 4. Finalize the run. Final tools (save_ticket_result / escalate_to_human) already set Outcome +
        //    CompletedAtUtc on this same tracked entity; if the agent stopped earlier (e.g. asked for
        //    clarification), fall back to its summary.
        run.Outcome ??= Truncate(response.Text, 1000);
        run.CompletedAtUtc ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var stepCount = await _db.AgentSteps.CountAsync(s => s.AgentRunId == run.Id, cancellationToken);

        foreach (var step in await _db.AgentSteps
                     .Where(s => s.AgentRunId == run.Id)
                     .OrderBy(s => s.StepIndex)
                     .ToListAsync(cancellationToken))
        {
            _logger.LogInformation("Run {RunId} step {Index}: {Tool} -> {Message}",
                run.Id, step.StepIndex, step.ToolName ?? "(reasoning)", step.Message);
        }

        _logger.LogInformation("Triage run {RunId} finished for ticket {TicketId}: {StepCount} steps. Outcome: {Outcome}",
            run.Id, ticketId, stepCount, run.Outcome);

        return new TriageRunResult(run.Id, ticketId, run.Provider, run.Outcome, stepCount);
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

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
