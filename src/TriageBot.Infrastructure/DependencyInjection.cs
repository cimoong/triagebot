using Microsoft.Extensions.DependencyInjection;
using TriageBot.Core.Abstractions;
using TriageBot.Infrastructure.Agent;
using TriageBot.Infrastructure.Persistence;
using TriageBot.Infrastructure.Tools;

namespace TriageBot.Infrastructure;

/// <summary>
/// Single composition-root entry point for the Infrastructure layer. The Web host calls
/// <c>AddInfrastructure()</c> and stays unaware of concrete implementations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Tools
        services.AddSingleton<KeywordClassifierTool>();

        // Services (placeholder implementations — swapped for LLM/EF Core later)
        services.AddScoped<ITriageService, HeuristicTriageService>();
        services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();

        return services;
    }
}
