using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Ai;

namespace TriageBot.Infrastructure.Agent;

/// <summary>
/// The core triage agent (Microsoft Agent Framework). Built on the active provider's
/// <see cref="IChatClient"/> and the four ticket tools, it classifies, drafts a reply, then
/// either escalates or finalizes — driving the tools through automatic function invocation.
/// </summary>
public sealed class TicketTriageAgent
{
    private readonly IAiClientResolver _resolver;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TicketTriageAgent> _logger;

    public TicketTriageAgent(IAiClientResolver resolver, ILoggerFactory loggerFactory)
    {
        _resolver = resolver;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TicketTriageAgent>();
    }

    private const string Instructions =
        """
        You are TriageBot, an IT support ticket triage agent. Process each ticket in this exact order:

        1. Decide the category and urgency, then call record_classification with your decision and a short reasoning.
        2. Write a professional, concise reply to the requester, then call draft_reply to save that text.
        3. If the urgency is Critical, or the issue needs another team or a human decision, call escalate_to_human
           with a clear reason. Otherwise finalize the ticket by calling save_ticket_result with status Resolved.

        Rules:
        - Always use the ticket id given in the message for every tool call.
        - Keep the reply professional, concise and helpful.
        - Do not invent facts (account names, internal ticket numbers, fixes you cannot know). If key information is
          missing, use draft_reply to ask the requester for the specific details needed, and do NOT resolve the ticket.
        - Stop once you have classified the ticket, saved a draft reply, and either escalated or finalized it.
        """;

    /// <summary>
    /// Runs the agent over <paramref name="ticket"/> using the given tools (bound to the current run).
    /// The tools persist their own results and audit steps; this returns the agent's response for the service to summarize.
    /// </summary>
    public async Task<AgentResponse> RunAsync(
        Ticket ticket, AiProvider provider, IList<AITool> tools, CancellationToken cancellationToken = default)
    {
        var chatClient = _resolver.GetChatClient(provider);

        var agentOptions = new ChatClientAgentOptions
        {
            Name = "TicketTriageAgent",
            // The provider's IChatClient already includes function invocation + logging (and the iteration cap),
            // so use it as-is rather than letting the agent wrap it a second time.
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = tools
            }
        };

        var agent = new ChatClientAgent(chatClient, agentOptions, _loggerFactory);

        // A fresh session is the agent's memory for this single run (holds the multi-step thread).
        var session = await agent.CreateSessionAsync(cancellationToken);

        // qwen3 (local default) is a reasoning model whose "thinking" chains are very slow on CPU and can
        // blow past the request timeout. "/no_think" disables that mode for the turn; other providers ignore it.
        var noThink = provider == AiProvider.Local ? "/no_think\n" : string.Empty;

        var prompt =
            $"""
             {noThink}Triage this IT support ticket.

             Ticket id: {ticket.Id}
             From: {ticket.RequesterEmail}
             Subject: {ticket.Subject}

             Body:
             {ticket.Body}
             """;

        _logger.LogInformation("Agent run starting for ticket {TicketId} using provider {Provider}.", ticket.Id, provider);

        var response = await agent.RunAsync(prompt, session, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Agent run finished for ticket {TicketId}: {MessageCount} messages, finish reason {FinishReason}.",
            ticket.Id, response.Messages.Count, response.FinishReason);

        return response;
    }
}
