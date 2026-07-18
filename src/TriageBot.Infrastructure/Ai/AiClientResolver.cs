using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using TriageBot.Core.Enums;

namespace TriageBot.Infrastructure.Ai;

/// <summary>
/// Looks up the keyed <see cref="IChatClient"/> registered under "local" / "gemini" and tracks the
/// active provider via the scoped <see cref="AiProviderState"/>.
/// </summary>
public sealed class AiClientResolver : IAiClientResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AiProviderState _state;

    public AiClientResolver(IServiceProvider serviceProvider, AiProviderState state)
    {
        _serviceProvider = serviceProvider;
        _state = state;
    }

    public AiProvider ActiveProvider => _state.Current;

    public IChatClient GetChatClient(AiProvider provider)
        => _serviceProvider.GetRequiredKeyedService<IChatClient>(KeyFor(provider));

    public IChatClient GetActiveChatClient() => GetChatClient(_state.Current);

    public IChatClient GetClassificationChatClient(AiProvider provider) => provider switch
    {
        // Groq has a small, cheap, cached model dedicated to classification (cost optimization).
        AiProvider.Groq => _serviceProvider.GetRequiredKeyedService<IChatClient>(GroqClassificationKey),
        // Single-model providers classify with their main model.
        _ => GetChatClient(provider)
    };

    /// <summary>DI key for the small, cached Groq classification client.</summary>
    internal const string GroqClassificationKey = "groq-classify";

    /// <summary>Stable DI keys used when registering the keyed chat clients.</summary>
    internal static string KeyFor(AiProvider provider) => provider switch
    {
        AiProvider.Local => "local",
        AiProvider.Gemini => "gemini",
        AiProvider.Groq => "groq",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown AI provider.")
    };
}
