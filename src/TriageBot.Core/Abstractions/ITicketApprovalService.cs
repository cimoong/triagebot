namespace TriageBot.Core.Abstractions;

/// <summary>Result of a human approval decision on a ticket's pending final action.</summary>
public sealed record ApprovalResult(
    Guid TicketId,
    string Status,
    string Message);

/// <summary>
/// Resolves the human-in-the-loop gate: executes or cancels the final action the agent left pending.
/// Deterministic (no LLM) and idempotent — approving/rejecting an already-finalized ticket is a safe no-op.
/// </summary>
public interface ITicketApprovalService
{
    /// <summary>
    /// Approves the pending final action and executes it. If <paramref name="editedDraft"/> is provided,
    /// the draft reply is updated first. Returns null if the ticket does not exist.
    /// </summary>
    Task<ApprovalResult?> ApproveAsync(Guid ticketId, string? editedDraft = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects the pending final action (it is not executed) and marks the ticket Rejected.
    /// Returns null if the ticket does not exist.
    /// </summary>
    Task<ApprovalResult?> RejectAsync(Guid ticketId, string? reason = null, CancellationToken cancellationToken = default);
}
