using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;

namespace TriageBot.Infrastructure.Tools;

/// <summary>
/// Placeholder classifier: a deterministic, keyword-based heuristic used as a stand-in
/// until the LLM-backed classifier is wired in. Being deterministic, it is trivially unit testable.
/// </summary>
public sealed class KeywordClassifierTool : IAgentTool<Ticket, (TicketCategory Category, TicketPriority Priority)>
{
    public string Name => "keyword_classifier";

    public string Description => "Classifies a ticket into a category and priority using simple keyword heuristics.";

    private static readonly Dictionary<TicketCategory, string[]> CategoryKeywords = new()
    {
        [TicketCategory.Hardware] = ["laptop", "monitor", "keyboard", "printer", "battery", "screen"],
        [TicketCategory.Software] = ["install", "update", "license", "crash", "bug", "application"],
        [TicketCategory.Network]  = ["vpn", "wifi", "wi-fi", "internet", "connection", "dns"],
        [TicketCategory.Account]  = ["password", "login", "locked", "reset", "access", "account"],
        [TicketCategory.Security] = ["phishing", "malware", "virus", "breach", "suspicious", "ransomware"],
    };

    private static readonly string[] CriticalKeywords = ["breach", "ransomware", "outage", "down", "production"];
    private static readonly string[] HighKeywords = ["urgent", "asap", "locked", "cannot work", "blocked"];

    public Task<(TicketCategory Category, TicketPriority Priority)> ExecuteAsync(
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

        var priority = TicketPriority.Low;
        if (CriticalKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            priority = TicketPriority.Critical;
        else if (HighKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            priority = TicketPriority.High;
        else if (category is TicketCategory.Security or TicketCategory.Account)
            priority = TicketPriority.Medium;

        return Task.FromResult((category, priority));
    }
}
