using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TriageBot.Core.Abstractions;
using TriageBot.Infrastructure.Agent;
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

        // Tools
        services.AddSingleton<KeywordClassifierTool>();

        // Services (placeholder implementations — swapped for LLM later)
        services.AddScoped<ITriageService, HeuristicTriageService>();
        services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();

        return services;
    }
}
