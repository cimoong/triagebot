using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Agent;
using TriageBot.Infrastructure.Tools;
using Xunit;

namespace TriageBot.Tests;

public class HeuristicTriageServiceTests
{
    private readonly HeuristicTriageService _sut = new(new KeywordClassifierTool());

    [Fact]
    public async Task Produces_a_draft_reply_and_classification()
    {
        var ticket = new Ticket { Subject = "Password reset", Body = "I am locked out of my account." };

        var result = await _sut.TriageAsync(ticket);

        Assert.Equal(TicketCategory.AccountAccess, result.Category);
        Assert.False(string.IsNullOrWhiteSpace(result.DraftReply));
    }

    [Fact]
    public async Task Escalates_critical_tickets()
    {
        var ticket = new Ticket { Subject = "Production outage", Body = "The billing system is down for all users." };

        var result = await _sut.TriageAsync(ticket);

        Assert.True(result.ShouldEscalate);
    }
}
