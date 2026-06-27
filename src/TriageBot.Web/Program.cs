using Microsoft.Extensions.AI;
using TriageBot.Core.Abstractions;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure;
using TriageBot.Infrastructure.Ai;
using TriageBot.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register the EF Core DbContext, triage services, tools and repositories.
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
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
        return Results.BadRequest(new { error = $"Unknown provider '{provider}'. Use 'local' or 'gemini'." });

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
    Guid id, ITicketTriageService triage, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    try
    {
        var result = await triage.ProcessTicketAsync(id, ct);
        return result is null
            ? Results.NotFound(new { error = $"Ticket {id} was not found." })
            : Results.Ok(result);
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("TicketProcessing")
            .LogError(ex, "Triage run failed for ticket {TicketId}.", id);
        return Results.Json(
            new { ticketId = id, error = $"The triage agent could not complete. Is the AI provider running? ({ex.Message})" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
