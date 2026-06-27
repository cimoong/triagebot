namespace TriageBot.Core.Abstractions;

/// <summary>Outcome of one agent run over a ticket, safe to return from an API (no navigation cycles).</summary>
public sealed record TriageRunResult(
    Guid RunId,
    Guid TicketId,
    string Provider,
    string? Outcome,
    int StepCount,
    bool AwaitingApproval,
    string? PendingAction);

/// <summary>
/// Orchestrates a full agent triage pass over a single ticket: creates an AgentRun, runs the agent,
/// records every step. When the agent proposes a final action (resolve/escalate) it pauses for human
/// approval — the run stops with the proposed action persisted, to be approved or rejected separately.
/// </summary>
public interface ITicketTriageService
{
    /// <summary>Runs the triage agent over the ticket. Returns null if the ticket does not exist.</summary>
    Task<TriageRunResult?> ProcessTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
