namespace TriageBot.Core.Abstractions;

/// <summary>
/// A discrete capability the triage agent can invoke (classify, draft a reply, look up a KB article, ...).
/// Tools are kept small and side-effect-explicit so they can be unit tested and gated behind human approval.
/// </summary>
/// <typeparam name="TInput">Strongly-typed input for the tool.</typeparam>
/// <typeparam name="TOutput">Strongly-typed result of running the tool.</typeparam>
public interface IAgentTool<in TInput, TOutput>
{
    /// <summary>Stable identifier used when exposing the tool to the LLM/agent runtime.</summary>
    string Name { get; }

    /// <summary>Human-readable description of what the tool does (also surfaced to the agent).</summary>
    string Description { get; }

    Task<TOutput> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}
