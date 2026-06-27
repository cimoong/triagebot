using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Tools;

namespace TriageBot.Infrastructure.Agent;

/// <summary>
/// Placeholder <see cref="ITriageService"/> that composes the keyword classifier and a templated draft reply.
/// It is swapped for an LLM-backed agent later; the Web layer depends only on the interface, so nothing else changes.
/// </summary>
public sealed class HeuristicTriageService : ITriageService
{
    private readonly KeywordClassifierTool _classifier;

    public HeuristicTriageService(KeywordClassifierTool classifier) => _classifier = classifier;

    public async Task<TriageResult> TriageAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        var (category, urgency) = await _classifier.ExecuteAsync(ticket, cancellationToken);

        var shouldEscalate = urgency is TicketUrgency.Critical;

        var draft =
            $"Hi,\n\nThanks for reaching out about \"{ticket.Subject}\". " +
            $"We've logged this as a {category} issue with {urgency} urgency and a member of the IT team " +
            (shouldEscalate
                ? "will escalate it for immediate attention."
                : "will follow up shortly.") +
            "\n\nRegards,\nIT Support";

        var reasoning = $"Classified as {category}/{urgency} via keyword heuristics; escalate={shouldEscalate}.";

        return new TriageResult(category, urgency, draft, shouldEscalate, reasoning);
    }
}
