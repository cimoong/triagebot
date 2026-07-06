using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using OpenAI;
using Polly;
using TriageBot.Core.Enums;

namespace TriageBot.Infrastructure.Ai;

/// <summary>
/// Registers two runtime-switchable <see cref="IChatClient"/>s via Microsoft.Extensions.AI:
/// keyed "local" (Ollama, default) and "gemini" (optional). Both talk to OpenAI-compatible endpoints.
/// </summary>
public static class AiServiceCollectionExtensions
{
    private const string LocalHttpClient = "ai-local";
    private const string GeminiHttpClient = "ai-gemini";
    private const string GroqHttpClient = "ai-groq";

    /// <summary>Hard cap on the agent's tool-calling loop so a confused model can't loop forever.</summary>
    private const int MaxToolIterations = 10;

    public static IServiceCollection AddAiProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));
        services.Configure<LocalAiOptions>(configuration.GetSection(LocalAiOptions.Section));
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.Section));
        services.Configure<GroqOptions>(configuration.GetSection(GroqOptions.Section));

        // Named HttpClients let us give local inference a generous timeout (slow CPU).
        services.AddHttpClient(LocalHttpClient, (sp, http) =>
                http.Timeout = TimeSpan.FromSeconds(
                    sp.GetRequiredService<IOptions<LocalAiOptions>>().Value.TimeoutSeconds))
            .AddLlmResilience();

        services.AddHttpClient(GeminiHttpClient, (sp, http) =>
                http.Timeout = TimeSpan.FromSeconds(
                    sp.GetRequiredService<IOptions<GeminiOptions>>().Value.TimeoutSeconds))
            .AddLlmResilience();

        services.AddHttpClient(GroqHttpClient, (sp, http) =>
                http.Timeout = TimeSpan.FromSeconds(
                    sp.GetRequiredService<IOptions<GroqOptions>>().Value.TimeoutSeconds))
            .AddLlmResilience();

        // Keyed chat client: Local (Ollama). No API key needed; "ollama" is a placeholder credential.
        services.AddKeyedChatClient(AiClientResolver.KeyFor(AiProvider.Local), sp =>
            {
                var o = sp.GetRequiredService<IOptions<LocalAiOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(LocalHttpClient);
                return BuildOpenAiCompatibleClient(o.Endpoint, o.ApiKey, o.ChatModel, http);
            })
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = MaxToolIterations)
            .UseLogging();

        // Keyed chat client: Gemini (optional). Fails with a clear message only when actually used without a key.
        services.AddKeyedChatClient(AiClientResolver.KeyFor(AiProvider.Gemini), sp =>
            {
                var o = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
                if (string.IsNullOrWhiteSpace(o.ApiKey))
                {
                    throw new InvalidOperationException(
                        "The Gemini provider was selected but 'Gemini:ApiKey' is not configured. " +
                        "Set it via user-secrets:  dotnet user-secrets set \"Gemini:ApiKey\" \"<your-key>\"  " +
                        "(run from src/TriageBot.Web). The Local (Ollama) provider does not require an API key.");
                }

                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(GeminiHttpClient);
                return BuildOpenAiCompatibleClient(o.Endpoint, o.ApiKey, o.ChatModel, http);
            })
            .UseFunctionInvocation()
            .UseLogging();

        // Keyed chat client: Groq (optional). Fails with a clear message only when actually used without a key.
        services.AddKeyedChatClient(AiClientResolver.KeyFor(AiProvider.Groq), sp =>
            {
                var o = sp.GetRequiredService<IOptions<GroqOptions>>().Value;
                if (string.IsNullOrWhiteSpace(o.ApiKey))
                {
                    throw new InvalidOperationException(
                        "The Groq provider was selected but 'Groq:ApiKey' is not configured. " +
                        "Set it via user-secrets:  dotnet user-secrets set \"Groq:ApiKey\" \"<your-key>\"  " +
                        "(run from src/TriageBot.Web), or the 'Groq__ApiKey' environment variable. " +
                        "The Local (Ollama) provider does not require an API key.");
                }

                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(GroqHttpClient);
                return BuildOpenAiCompatibleClient(o.Endpoint, o.ApiKey, o.ChatModel, http);
            })
            .UseFunctionInvocation()
            .UseLogging();

        // Per-session active provider (scoped) + resolver.
        services.AddScoped(sp =>
            new AiProviderState(sp.GetRequiredService<IOptions<AiOptions>>().Value.DefaultProvider));
        services.AddScoped<IAiClientResolver, AiClientResolver>();

        return services;
    }

    /// <summary>
    /// Builds an <see cref="IChatClient"/> over any OpenAI-compatible endpoint (Ollama, Gemini, ...).
    /// The pipeline's <see cref="ClientPipelineOptions.NetworkTimeout"/> is aligned with the HttpClient
    /// timeout, otherwise System.ClientModel's default (100s) would cut long local generations short.
    /// </summary>
    private static IChatClient BuildOpenAiCompatibleClient(string endpoint, string apiKey, string model, HttpClient httpClient)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = httpClient.Timeout,
            Transport = new HttpClientPipelineTransport(httpClient)
        };

        return new OpenAIClient(new ApiKeyCredential(apiKey), options)
            .GetChatClient(model)
            .AsIChatClient();
    }

    /// <summary>
    /// Adds a retry-only resilience handler for transient failures (connection refused, 5xx, 408/429,
    /// <see cref="HttpRequestException"/>). It deliberately does NOT add a pipeline timeout: the per-request
    /// timeout is already governed by <see cref="HttpClient.Timeout"/> (generous for slow local inference),
    /// and the default predicate does not retry that cancellation — so a legitimately slow generation runs to
    /// completion instead of triggering a retry storm, while a momentarily unavailable provider is retried.
    /// </summary>
    private static void AddLlmResilience(this IHttpClientBuilder builder) =>
        builder.AddResilienceHandler("llm-retry", pipeline =>
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            }));
}
