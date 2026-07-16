using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using OpenAI;
using Polly;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Observability;

namespace TriageBot.Infrastructure.Ai;

/// <summary>
/// Registers the runtime-switchable <see cref="IChatClient"/>s via Microsoft.Extensions.AI: keyed
/// "local" (Ollama, default), "gemini" and "groq" (optional), plus a small, cached "groq-classify"
/// client used for cheap classification. All talk to OpenAI-compatible endpoints.
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

        // In-memory IDistributedCache backs response caching for the (pure, side-effect-free)
        // classification calls — identical ticket text is classified once, not on every run.
        services.AddDistributedMemoryCache();

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
            // Emit gen_ai.* spans + token-usage metrics per model call. EnableSensitiveData=false keeps
            // prompt/response content (and any PII in ticket text) out of telemetry.
            .UseOpenTelemetry(sourceName: TriageBotTelemetry.ChatSourceName, configure: c => c.EnableSensitiveData = false)
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
            .UseOpenTelemetry(sourceName: TriageBotTelemetry.ChatSourceName, configure: c => c.EnableSensitiveData = false)
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
            .UseOpenTelemetry(sourceName: TriageBotTelemetry.ChatSourceName, configure: c => c.EnableSensitiveData = false)
            .UseLogging();

        // Cost optimization: a SMALL Groq model dedicated to classification (light reasoning), with
        // response caching. Classification is a single, tool-free, side-effect-free call, so caching is
        // safe here: identical ticket text returns the cached category/urgency with no LLM call.
        // (We do NOT cache the agent's drafting loop — its tool calls have side effects, so a cache hit
        // would wrongly skip the DB writes.) No UseFunctionInvocation: classification uses no tools.
        services.AddKeyedChatClient(AiClientResolver.GroqClassificationKey, sp =>
            {
                var o = sp.GetRequiredService<IOptions<GroqOptions>>().Value;
                if (string.IsNullOrWhiteSpace(o.ApiKey))
                {
                    throw new InvalidOperationException(
                        "The Groq provider was selected but 'Groq:ApiKey' is not configured. " +
                        "Set it via user-secrets or the 'Groq__ApiKey' environment variable.");
                }

                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(GroqHttpClient);
                return BuildOpenAiCompatibleClient(o.Endpoint, o.ApiKey, o.ClassificationModel, http);
            })
            // Cache OUTERMOST so a hit short-circuits before the telemetry/model layers (0 tokens on hit).
            .Use((inner, provider) => new DistributedCachingChatClient(inner, provider.GetRequiredService<IDistributedCache>())
            {
                // Scope the key to this model so a cached classification is never served for another model.
                CacheKeyAdditionalValues = new object[] { AiClientResolver.GroqClassificationKey }
            })
            .UseOpenTelemetry(sourceName: TriageBotTelemetry.ChatSourceName, configure: c => c.EnableSensitiveData = false)
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
            Transport = new HttpClientPipelineTransport(httpClient),
            // Disable the OpenAI SDK's own retry policy: our Polly resilience handler (AddLlmResilience) is the
            // single retry layer. Without this, the SDK retries on top of Polly, so one stalled request under a
            // provider rate limit becomes 4 SDK tries x the HttpClient timeout (e.g. ~8 min) instead of failing
            // fast. maxRetries:0 makes a rate-limited/slow call surface quickly.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
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
