using TriageBot.Core.Domain;

namespace TriageBot.Core.Abstractions;

/// <summary>
/// Orchestrates the triage workflow for a ticket. The implementation coordinates the agent/LLM
/// and the available <see cref="IAgentTool{TInput,TOutput}"/>s to produce a <see cref="TriageResult"/>.
/// </summary>
public interface ITriageService
{
    /// <summary>
    /// Analyzes a ticket and returns a proposed classification and draft reply.
    /// The result is a suggestion only — applying it requires explicit human approval.
    /// </summary>
    Task<TriageResult> TriageAsync(Ticket ticket, CancellationToken cancellationToken = default);
}
