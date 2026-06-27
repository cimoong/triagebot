using TriageBot.Core.Enums;

namespace TriageBot.Infrastructure.Ai;

/// <summary>
/// Holds the currently selected <see cref="AiProvider"/> for a single scope.
/// Registered as <c>Scoped</c> so each Blazor Server circuit (user session) switches providers
/// independently without affecting other connected users.
/// </summary>
public sealed class AiProviderState
{
    private AiProvider _current;

    public AiProviderState(AiProvider initial) => _current = initial;

    /// <summary>Raised whenever <see cref="Current"/> changes, so the UI can re-render.</summary>
    public event Action? Changed;

    public AiProvider Current
    {
        get => _current;
        set
        {
            if (_current == value)
                return;

            _current = value;
            Changed?.Invoke();
        }
    }
}
