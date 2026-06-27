namespace TriageBot.Core.Abstractions;

/// <summary>Outcome of one agent run over a ticket, safe to return from an API (no navigation cycles).</summary>
public sealed record TriageRunResult(
    Guid RunId,
    Guid TicketId,
    string Provider,
    string? Outcome,
    int StepCount);

/// <summary>
/// Orchestrates a full agent triage pass over a single ticket: creates an AgentRun, runs the agent,
/// records every step, and finalizes the run. Final actions still happen here — human approval is layered on later.
/// </summary>
public interface ITicketTriageService
{
    /// <summary>Runs the triage agent over the ticket. Returns null if the ticket does not exist.</summary>
    Task<TriageRunResult?> ProcessTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
