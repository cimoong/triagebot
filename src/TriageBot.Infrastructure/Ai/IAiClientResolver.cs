using Microsoft.Extensions.AI;
using TriageBot.Core.Enums;

namespace TriageBot.Infrastructure.Ai;

/// <summary>
/// Resolves the right keyed <see cref="IChatClient"/> for a provider, and exposes the
/// session's active provider (from <see cref="AiProviderState"/>).
/// </summary>
public interface IAiClientResolver
{
    /// <summary>The provider currently selected for this scope/session.</summary>
    AiProvider ActiveProvider { get; }

    /// <summary>Returns the chat client for an explicit provider.</summary>
    IChatClient GetChatClient(AiProvider provider);

    /// <summary>Returns the chat client for the session's active provider.</summary>
    IChatClient GetActiveChatClient();
}
