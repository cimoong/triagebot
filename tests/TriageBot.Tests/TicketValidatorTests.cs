using TriageBot.Core.Domain;
using Xunit;

namespace TriageBot.Tests;

/// <summary>Guardrail: input validation + sanitization for untrusted ticket text.</summary>
public class TicketValidatorTests
{
    [Theory]
    [InlineData("", "A real body that is long enough.", "u@x.com")]       // empty subject
    [InlineData("ab", "A real body that is long enough.", "u@x.com")]     // subject too short
    [InlineData("Valid subject", "", "u@x.com")]                          // empty body
    [InlineData("Valid subject", "hi", "u@x.com")]                        // body too short
    [InlineData("Valid subject", "A real body here.", "")]                // empty email
    [InlineData("Valid subject", "A real body here.", "not-an-email")]    // malformed email
    [InlineData("   ", "A real body here.", "u@x.com")]                   // whitespace-only subject
    public void Rejects_invalid_input(string subject, string body, string email)
    {
        var result = TicketValidator.Validate(subject, body, email);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Rejects_overlong_subject_and_body()
    {
        var longSubject = new string('a', TicketValidator.MaxSubjectLength + 1);
        var longBody = new string('b', TicketValidator.MaxBodyLength + 1);

        Assert.False(TicketValidator.Validate(longSubject, "A real body here.", "u@x.com").IsValid);
        Assert.False(TicketValidator.Validate("Valid subject", longBody, "u@x.com").IsValid);
    }

    [Fact]
    public void Accepts_well_formed_input()
    {
        var result = TicketValidator.Validate(
            "VPN keeps disconnecting", "The corporate VPN drops every few minutes from home.", "user@contoso.com");

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Sanitize_strips_control_characters_but_keeps_newlines_and_tabs()
    {
        // NUL + BEL are stripped; tab/newline survive; surrounding whitespace is trimmed.
        var dirty = "  Hello\0\aworld\tnext\nline  ";

        var clean = TicketValidator.Sanitize(dirty);

        Assert.Equal("Helloworld\tnext\nline", clean);
    }

    [Fact]
    public void Sanitize_null_or_empty_returns_empty()
    {
        Assert.Equal(string.Empty, TicketValidator.Sanitize(null));
        Assert.Equal(string.Empty, TicketValidator.Sanitize(""));
    }
}
