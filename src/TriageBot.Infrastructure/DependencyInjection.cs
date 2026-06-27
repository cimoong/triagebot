using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TriageBot.Core.Abstractions;
using TriageBot.Infrastructure.Agent;
using TriageBot.Infrastructure.Ai;
using TriageBot.Infrastructure.Persistence;
using TriageBot.Infrastructure.Tools;

namespace TriageBot.Infrastructure;

/// <summary>
/// Single composition-root entry point for the Infrastructure layer. The Web host calls
/// <c>AddInfrastructure(configuration)</c> and stays unaware of concrete implementations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TriageBotDb")
            ?? throw new InvalidOperationException("Connection string 'TriageBotDb' was not found.");

        services.AddDbContext<TriageBotDbContext>(options => options.UseNpgsql(connectionString));

        // Runtime-switchable LLM providers (Local/Ollama default, Gemini optional).
        services.AddAiProviders(configuration);

        // Core triage agent (Microsoft Agent Framework) + orchestration service.
        services.AddScoped<TicketTriageAgent>();
        services.AddScoped<ITicketTriageService, TicketTriageService>();
        services.AddScoped<ITicketApprovalService, TicketApprovalService>();

        // Heuristic placeholders kept for reference / tests.
        services.AddSingleton<KeywordClassifierTool>();
        services.AddScoped<ITriageService, HeuristicTriageService>();
        services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();

        return services;
    }
}
