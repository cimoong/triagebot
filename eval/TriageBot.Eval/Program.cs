using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;
using TriageBot.Core.Enums;
using TriageBot.Infrastructure.Agent;
using TriageBot.Infrastructure.Ai;
using TriageBot.Infrastructure.Persistence;
using TriageBot.Infrastructure.Tools;

// -----------------------------------------------------------------------------
// TriageBot eval harness.
// Runs the real triage agent over a labelled dataset and reports how often the
// agent's classification and escalation decision match the expected answer.
//
//   dotnet run --project eval/TriageBot.Eval -- [local|gemini] [path-to-tickets.json]
//
// Defaults: provider "local", dataset the tickets.json copied next to the binary.
// The Gemini key is read from user-secrets (shared with the web app) or the
// Gemini__ApiKey environment variable.
// -----------------------------------------------------------------------------

var provider = ParseProvider(args.ElementAtOrDefault(0), out var providerArgValid);
if (!providerArgValid)
{
    Console.Error.WriteLine($"Unknown provider '{args[0]}'. Use 'local' or 'gemini'.");
    return 2;
}

var datasetPath = args.ElementAtOrDefault(1)
    ?? Path.Combine(AppContext.BaseDirectory, "tickets.json");

if (!File.Exists(datasetPath))
{
    Console.Error.WriteLine($"Dataset not found: {datasetPath}");
    return 2;
}

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};

var cases = JsonSerializer.Deserialize<List<EvalCase>>(await File.ReadAllTextAsync(datasetPath), jsonOptions)
            ?? new List<EvalCase>();

if (cases.Count == 0)
{
    Console.Error.WriteLine("The dataset contains no cases.");
    return 2;
}

await using var services = BuildServices(provider);

Console.WriteLine();
Console.WriteLine($"TriageBot eval — provider: {provider}");
Console.WriteLine($"Dataset: {datasetPath} ({cases.Count} cases)");
Console.WriteLine(new string('-', 78));

int categoryHits = 0, urgencyHits = 0, escalateHits = 0, failures = 0;
double totalMs = 0;

for (var i = 0; i < cases.Count; i++)
{
    var c = cases[i];
    using var scope = services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AiProviderState>().Current = provider;

    var db = scope.ServiceProvider.GetRequiredService<TriageBotDbContext>();
    var triage = scope.ServiceProvider.GetRequiredService<ITicketTriageService>();

    var ticket = new Ticket { Subject = c.Subject, Body = c.Body, RequesterEmail = c.RequesterEmail };
    db.Tickets.Add(ticket);
    await db.SaveChangesAsync();

    var sw = Stopwatch.StartNew();
    TriageRunResult? result;
    try
    {
        result = await triage.ProcessTicketAsync(ticket.Id);
    }
    catch (Exception ex)
    {
        sw.Stop();
        failures++;
        Console.WriteLine($"{i + 1,2}. {Trim(c.Subject, 40),-40} FAILED: {ex.Message}");
        continue;
    }
    sw.Stop();
    totalMs += sw.Elapsed.TotalMilliseconds;

    var saved = await db.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id);
    var gotEscalate = result?.PendingAction == TicketTools.EscalateToHumanTool;

    var catOk = saved.Category == c.ExpectedCategory;
    var urgOk = saved.Urgency == c.ExpectedUrgency;
    var escOk = gotEscalate == c.ShouldEscalate;
    if (catOk) categoryHits++;
    if (urgOk) urgencyHits++;
    if (escOk) escalateHits++;

    Console.WriteLine($"{i + 1,2}. {Trim(c.Subject, 40),-40} " +
        $"cat {Mark(catOk)} {saved.Category?.ToString() ?? "—"}/{c.ExpectedCategory}  " +
        $"urg {Mark(urgOk)} {saved.Urgency?.ToString() ?? "—"}/{c.ExpectedUrgency}  " +
        $"esc {Mark(escOk)} {gotEscalate}/{c.ShouldEscalate}  " +
        $"{sw.Elapsed.TotalMilliseconds,6:F0} ms");
}

var scored = cases.Count - failures;
Console.WriteLine(new string('-', 78));
Console.WriteLine($"Category accuracy:   {categoryHits}/{cases.Count}");
Console.WriteLine($"Urgency accuracy:    {urgencyHits}/{cases.Count}");
Console.WriteLine($"Escalation accuracy: {escalateHits}/{cases.Count}");
if (failures > 0)
    Console.WriteLine($"Failed runs:         {failures}/{cases.Count} (provider unreachable or errored)");
Console.WriteLine($"Avg latency:         {(scored > 0 ? totalMs / scored : 0),0:F0} ms over {scored} successful runs");
Console.WriteLine();

return 0;

static string Mark(bool ok) => ok ? "✓" : "✗";
static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

static AiProvider ParseProvider(string? arg, out bool valid)
{
    if (string.IsNullOrWhiteSpace(arg))
    {
        valid = true;
        return AiProvider.Local;
    }
    valid = Enum.TryParse<AiProvider>(arg, ignoreCase: true, out var p);
    return valid ? p : AiProvider.Local;
}

static ServiceProvider BuildServices(AiProvider provider)
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:DefaultProvider"] = provider.ToString()
        })
        .AddUserSecrets(typeof(EvalCase).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));

    // In-memory database so the eval needs no Postgres; the agent's tools persist here transparently.
    services.AddDbContext<TriageBotDbContext>(o => o.UseInMemoryDatabase("triagebot-eval"));

    services.AddAiProviders(configuration);
    services.AddScoped<TicketTriageAgent>();
    services.AddScoped<ITicketTriageService, TicketTriageService>();

    return services.BuildServiceProvider();
}

/// <summary>One labelled ticket: the input plus the expected triage outcome.</summary>
internal sealed record EvalCase
{
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required string RequesterEmail { get; init; }
    public required TicketCategory ExpectedCategory { get; init; }
    public required TicketUrgency ExpectedUrgency { get; init; }
    public required bool ShouldEscalate { get; init; }
}
