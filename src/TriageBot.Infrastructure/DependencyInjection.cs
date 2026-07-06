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
        // Connection string always comes from configuration. In production it is supplied via the
        // environment variable ConnectionStrings__TriageBotDb (12-factor); appsettings only carries a
        // local dev value. There is no hard-coded production fallback, so credentials never ship in the app.
        var rawConnectionString = configuration.GetConnectionString("TriageBotDb")
            ?? throw new InvalidOperationException(
                "Connection string 'TriageBotDb' was not found. Set the ConnectionStrings__TriageBotDb " +
                "environment variable (accepts a Neon/Postgres URL or Npgsql key-value form).");

        // Accept either a managed-provider URL (postgresql://…) or Npgsql key-value form.
        var connectionString = NpgsqlConnectionString.Normalize(rawConnectionString);

        services.AddDbContext<TriageBotDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Serverless Postgres (e.g. Neon) idle-suspends and cold-starts; retry transient
                // failures so the first request after a wake-up doesn't fail. Also bound command time.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 8,
                    maxRetryDelay: TimeSpan.FromSeconds(15),
                    errorCodesToAdd: null);
                npgsql.CommandTimeout(30);
            }));

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
