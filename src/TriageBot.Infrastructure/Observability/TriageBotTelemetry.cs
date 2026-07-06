namespace TriageBot.Infrastructure.Observability;

/// <summary>
/// Names of the OpenTelemetry <see cref="System.Diagnostics.ActivitySource"/> / Meter used to instrument
/// the triage agent and the underlying LLM chat calls. The same names are used at registration time
/// (agent + chat-client pipelines) and when wiring the exporter (AddSource / AddMeter) in the web host,
/// so keep them in one place.
/// </summary>
public static class TriageBotTelemetry
{
    /// <summary>Source for agent-level spans (one span per <c>RunAsync</c>, wrapping the LLM/tool spans).</summary>
    public const string AgentSourceName = "TriageBot.Agent";

    /// <summary>Source/Meter for LLM chat spans and token-usage metrics (gen_ai.* semantic conventions).</summary>
    public const string ChatSourceName = "TriageBot.Llm";
}
