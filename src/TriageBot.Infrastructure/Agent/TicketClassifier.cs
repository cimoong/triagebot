using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Ai;
using TriageBot.Infrastructure.Tools;

namespace TriageBot.Infrastructure.Agent;

/// <summary>
/// Classifies a ticket (category + urgency) with a single, tool-free structured-output call on a
/// lightweight model (cost optimization: Groq uses <c>llama-3.1-8b-instant</c>; other providers use
/// their main model). This is deliberately NOT part of the agent's tool loop — classification is a plain
/// single call, which is cheaper, cacheable, and reliable on small models (no tool-calling needed).
/// The result is persisted via <see cref="TicketTools.RecordClassificationAsync"/> so the audit trail is
/// identical to before.
/// </summary>
public sealed class TicketClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Enums as strings so the model returns/parses readable values (Network, Critical, ...).
        Converters = { new JsonStringEnumConverter() },
        // Required: MEAI marks these options read-only, so a type-info resolver must be set explicitly
        // (reflection-based is fine here — this app is not trimmed/AOT).
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private const int MaxClassificationTokens = 256;

    private readonly IAiClientResolver _resolver;
    private readonly ILogger<TicketClassifier> _logger;

    public TicketClassifier(IAiClientResolver resolver, ILogger<TicketClassifier> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>The model's decision. Kept tiny to bound output tokens.</summary>
    private sealed record Classification(TicketCategory Category, TicketUrgency Urgency, string? Reasoning);

    /// <summary>
    /// Classifies the ticket and records it on the run. Returns the LLM token usage (for cost logging),
    /// or null if the provider did not report usage.
    /// </summary>
    public async Task<UsageDetails?> ClassifyAndRecordAsync(
        Ticket ticket, AiProvider provider, TicketTools tools, CancellationToken cancellationToken = default)
    {
        var client = _resolver.GetClassificationChatClient(provider);

        // NOTE: the prompt intentionally excludes the ticket id, so two runs of the *same* ticket text hit
        // the response cache. Keep it short to minimize input tokens.
        var prompt =
            $"""
             Classify this IT support ticket. Pick the single best category and urgency, and give a one-sentence reason.

             Subject: {ticket.Subject}
             Body:
             {ticket.Body}
             """;

        var options = new ChatOptions { MaxOutputTokens = MaxClassificationTokens };

        ChatResponse<Classification> response;
        try
        {
            // useJsonSchema:false -> JSON-object mode with the schema in the prompt, instead of the native
            // json_schema response format. Small Groq models (llama-3.1-8b-instant) don't support json_schema.
            response = await client.GetResponseAsync<Classification>(prompt, JsonOptions, options, false, cancellationToken);
        }
        catch (Exception ex)
        {
            // Provider unreachable / timed out — let the orchestrator fail the run cleanly.
            _logger.LogError(ex, "Classification call failed for ticket {TicketId}.", ticket.Id);
            throw;
        }

        if (response.TryGetResult(out var result) && result is not null)
        {
            await tools.RecordClassificationAsync(
                ticket.Id, result.Category, result.Urgency,
                string.IsNullOrWhiteSpace(result.Reasoning) ? "Classified by the lightweight model." : result.Reasoning!,
                cancellationToken);
            _logger.LogInformation("Ticket {TicketId} classified as {Category}/{Urgency}.",
                ticket.Id, result.Category, result.Urgency);
        }
        else
        {
            // The small model returned unparseable output — don't fail the run; use a safe default and
            // let the human reviewer correct it. (Escalation still triggers on Critical if set later.)
            _logger.LogWarning("Ticket {TicketId}: classification result could not be parsed; defaulting to Other/Medium.", ticket.Id);
            await tools.RecordClassificationAsync(
                ticket.Id, TicketCategory.Other, TicketUrgency.Medium,
                "Automatic classification was inconclusive; defaulted for human review.", cancellationToken);
        }

        return response.Usage;
    }
}
