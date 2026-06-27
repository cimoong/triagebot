using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Tools;
using Xunit;

namespace TriageBot.Tests;

public class KeywordClassifierToolTests
{
    private readonly KeywordClassifierTool _sut = new();

    [Fact]
    public async Task Classifies_network_ticket_by_keyword()
    {
        var ticket = new Ticket { Subject = "VPN not connecting", Body = "My wifi is fine but the VPN drops." };

        var (category, _) = await _sut.ExecuteAsync(ticket);

        Assert.Equal(TicketCategory.Network, category);
    }

    [Fact]
    public async Task Flags_security_breach_as_critical()
    {
        var ticket = new Ticket { Subject = "Possible breach", Body = "Ransomware detected on a production server." };

        var (category, priority) = await _sut.ExecuteAsync(ticket);

        Assert.Equal(TicketCategory.Security, category);
        Assert.Equal(TicketPriority.Critical, priority);
    }

    [Fact]
    public async Task Defaults_to_other_and_low_when_no_keywords_match()
    {
        var ticket = new Ticket { Subject = "Hello", Body = "Just saying hi." };

        var (category, priority) = await _sut.ExecuteAsync(ticket);

        Assert.Equal(TicketCategory.Other, category);
        Assert.Equal(TicketPriority.Low, priority);
    }
}
