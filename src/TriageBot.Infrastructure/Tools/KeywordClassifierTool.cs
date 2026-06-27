using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;

namespace TriageBot.Infrastructure.Tools;

/// <summary>
/// Placeholder classifier: a deterministic, keyword-based heuristic used as a stand-in
/// until the LLM-backed classifier is wired in. Being deterministic, it is trivially unit testable.
/// </summary>
public sealed class KeywordClassifierTool : IAgentTool<Ticket, (TicketCategory Category, TicketUrgency Urgency)>
{
    public string Name => "keyword_classifier";

    public string Description => "Classifies a ticket into a category and urgency using simple keyword heuristics.";

    private static readonly Dictionary<TicketCategory, string[]> CategoryKeywords = new()
    {
        [TicketCategory.AccountAccess] = ["password", "login", "log in", "locked", "reset", "access", "account", "mfa", "2fa"],
        [TicketCategory.Network]       = ["vpn", "wifi", "wi-fi", "internet", "connection", "dns", "network"],
        [TicketCategory.Email]         = ["email", "outlook", "mailbox", "smtp", "inbox", "sync"],
        [TicketCategory.Software]      = ["install", "update", "license", "crash", "bug", "application", "app"],
        [TicketCategory.Hardware]      = ["laptop", "monitor", "keyboard", "printer", "battery", "screen", "mouse"],
    };

    private static readonly string[] CriticalKeywords = ["outage", "production", "down for all", "data loss", "breach", "ransomware"];
    private static readonly string[] HighKeywords = ["urgent", "asap", "locked", "cannot work", "blocked", "down"];

    public Task<(TicketCategory Category, TicketUrgency Urgency)> ExecuteAsync(
        Ticket input, CancellationToken cancellationToken = default)
    {
        var text = $"{input.Subject} {input.Body}".ToLowerInvariant();

        var category = TicketCategory.Other;
        foreach (var (cat, keywords) in CategoryKeywords)
        {
            if (keywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            {
                category = cat;
                break;
            }
        }

        var urgency = TicketUrgency.Low;
        if (CriticalKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            urgency = TicketUrgency.Critical;
        else if (HighKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            urgency = TicketUrgency.High;
        else if (category is TicketCategory.AccountAccess)
            urgency = TicketUrgency.Medium;

        return Task.FromResult((category, urgency));
    }
}
