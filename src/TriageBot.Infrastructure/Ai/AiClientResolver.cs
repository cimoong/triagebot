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

    /// <summary>Stable DI keys used when registering the keyed chat clients.</summary>
    internal static string KeyFor(AiProvider provider) => provider switch
    {
        AiProvider.Local => "local",
        AiProvider.Gemini => "gemini",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown AI provider.")
    };
}
