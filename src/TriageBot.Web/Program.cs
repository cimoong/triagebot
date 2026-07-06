using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TriageBot.Core.Abstractions;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure;
using TriageBot.Infrastructure.Agent;
using TriageBot.Infrastructure.Ai;
using TriageBot.Infrastructure.Observability;
using TriageBot.Infrastructure.Persistence;
using TriageBot.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Configuration is layered by the default host: appsettings*.json -> environment variables -> ...
// so every setting below can be supplied via env in a container using the "__" separator, e.g.
// ConnectionStrings__TriageBotDb, Groq__ApiKey, Ai__DefaultProvider. Nothing here reads config any
// other way, so there is no path that bypasses environment variables.

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register the EF Core DbContext, triage services, tools and repositories.
builder.Services.AddInfrastructure(builder.Configuration);

// RFC 7807 ProblemDetails for unhandled errors, so the API never leaks a stack trace.
builder.Services.AddProblemDetails();

// Optional observability: export traces, metrics and logs to Azure Application Insights via the
// Azure Monitor OpenTelemetry distro — ONLY when a connection string is provided
// (env APPLICATIONINSIGHTS_CONNECTION_STRING). Locally, with no connection string, this whole block
// is skipped: OpenTelemetry still records in-process (activities/meters) but nothing is exported and
// the app does not crash. The distro also captures ILogger logs and forwards them to App Insights.
var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry()
        // Agent-level spans + the LLM chat spans (gen_ai.* incl. token usage attributes).
        .WithTracing(tracing => tracing
            .AddSource(TriageBotTelemetry.AgentSourceName)
            .AddSource(TriageBotTelemetry.ChatSourceName))
        // Token-usage and other gen_ai metrics from the chat/agent meters.
        .WithMetrics(metrics => metrics
            .AddMeter(TriageBotTelemetry.AgentSourceName)
            .AddMeter(TriageBotTelemetry.ChatSourceName))
        // Azure Monitor exporter + default ASP.NET Core / HttpClient instrumentation + log export.
        .UseAzureMonitor(options => options.ConnectionString = appInsightsConnectionString);
}

var app = builder.Build();

// Log which database this instance targets — host + database only, never the password —
// so it's obvious at a glance whether it's pointing at local Postgres or a managed one (Neon).
{
    var rawConnectionString = app.Configuration.GetConnectionString("TriageBotDb");
    if (!string.IsNullOrWhiteSpace(rawConnectionString))
    {
        var b = new Npgsql.NpgsqlConnectionStringBuilder(NpgsqlConnectionString.Normalize(rawConnectionString));
        app.Logger.LogInformation("Database target: Host={Host}; Port={Port}; Database={Database}; SslMode={SslMode}",
            b.Host, b.Port, b.Database, b.SslMode);
    }
}

// Optional, opt-in EF Core migration on startup (env RunMigrationsOnStartup=true; default false).
// Trade-off: convenient for a single-instance demo, but risky in production — concurrent instances can
// race the migration, and app identities usually shouldn't hold schema-change rights. Prefer running
// `dotnet ef database update` (or a dedicated migration job/init container) as a separate deploy step.
if (app.Configuration.GetValue("RunMigrationsOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TriageBotDbContext>();
    app.Logger.LogInformation("RunMigrationsOnStartup=true — applying EF Core migrations...");
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("EF Core migrations applied.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// Convert unhandled exceptions on non-Razor (API) routes into RFC 7807 responses (no stack trace leaked).
app.UseStatusCodePages();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Quick connectivity probe for either LLM provider:
//   GET /health/ai?provider=local|gemini   (defaults to the session's active provider)
app.MapGet("/health/ai", async (string? provider, IAiClientResolver resolver, CancellationToken ct) =>
{
    AiProvider selected;
    if (string.IsNullOrWhiteSpace(provider))
        selected = resolver.ActiveProvider;
    else if (!Enum.TryParse(provider, ignoreCase: true, out selected))
        return Results.BadRequest(new { error = $"Unknown provider '{provider}'. Use 'local', 'gemini' or 'groq'." });

    try
    {
        var client = resolver.GetChatClient(selected);
        var response = await client.GetResponseAsync("reply with the word OK", cancellationToken: ct);
        return Results.Ok(new { provider = selected.ToString(), reply = response.Text });
    }
    catch (InvalidOperationException ex)
    {
        // e.g. Gemini selected without an API key — a configuration problem, not an outage.
        return Results.Json(new { provider = selected.ToString(), error = ex.Message },
            statusCode: StatusCodes.Status400BadRequest);
    }
    catch (Exception ex)
    {
        // e.g. Ollama not running / Gemini unreachable.
        return Results.Json(
            new
            {
                provider = selected.ToString(),
                error = $"Could not reach the {selected} AI provider. Is it running and reachable? ({ex.Message})"
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Run the triage agent over a ticket. T5: executes immediately (no approval gate yet — added in T6).
app.MapPost("/api/tickets/{id:guid}/process", async (
    Guid id, ITicketTriageService triage, ILoggerFactory loggerFactory, HttpContext httpContext) =>
{
    var logger = loggerFactory.CreateLogger("TicketProcessing");
    try
    {
        // Deliberately not tied to the request-abort token: a triage run mutates state and may pause for
        // approval, so a transient client disconnect must not abort it mid-run and strand the ticket.
        var result = await triage.ProcessTicketAsync(id, CancellationToken.None);
        return result is null
            ? Results.NotFound(new { error = $"Ticket {id} was not found." })
            : Results.Ok(result);
    }
    catch (TriageRunException ex) when (ex.IsRateLimited)
    {
        // The provider is reachable but its rate/token quota was exceeded — 429, not a 503 outage.
        logger.LogWarning(ex, "Triage run for ticket {TicketId} hit the provider rate limit.", id);
        httpContext.Response.Headers["Retry-After"] = "10";
        return Results.Json(
            new { ticketId = id, error = ex.Message, rateLimited = true },
            statusCode: StatusCodes.Status429TooManyRequests);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Triage run failed for ticket {TicketId}.", id);
        var message = ex is TriageRunException ? ex.Message
            : $"The triage agent could not complete. Is the AI provider running? ({ex.Message})";
        return Results.Json(
            new { ticketId = id, error = message },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Human-in-the-loop: approve the pending final action (optionally editing the draft first).
app.MapPost("/api/tickets/{id:guid}/approve", async (
    Guid id, ApproveRequest? body, ITicketApprovalService approval, CancellationToken ct) =>
{
    var result = await approval.ApproveAsync(id, body?.EditedDraft, ct);
    return result is null ? Results.NotFound(new { error = $"Ticket {id} was not found." }) : Results.Ok(result);
});

// Human-in-the-loop: reject the pending final action (it is not executed).
app.MapPost("/api/tickets/{id:guid}/reject", async (
    Guid id, RejectRequest? body, ITicketApprovalService approval, CancellationToken ct) =>
{
    var result = await approval.RejectAsync(id, body?.Reason, ct);
    return result is null ? Results.NotFound(new { error = $"Ticket {id} was not found." }) : Results.Ok(result);
});

app.Run();

internal sealed record ApproveRequest(string? EditedDraft);
internal sealed record RejectRequest(string? Reason);
