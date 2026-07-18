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
//   dotnet run --project eval/TriageBot.Eval -- [local|gemini|groq] [path-to-tickets.json]
//
// Defaults: provider "local", dataset the tickets.json copied next to the binary.
// A Gemini/Groq key is read from user-secrets (shared with the web app) or the
// Gemini__ApiKey / Groq__ApiKey environment variable.
// -----------------------------------------------------------------------------

// Separate positional args (provider, dataset path) from flags (--delay [value]) so they can appear in
// any order. A bare "--delay 30" consumes the following token as its value, so drop that token too.
var positionals = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--", StringComparison.Ordinal))
    {
        if (args[i] == "--delay" && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            i++; // skip the value that belongs to --delay
        continue;
    }
    positionals.Add(args[i]);
}

var provider = ParseProvider(positionals.ElementAtOrDefault(0), out var providerArgValid);
if (!providerArgValid)
{
    Console.Error.WriteLine($"Unknown provider '{positionals[0]}'. Use 'local', 'gemini' or 'groq'.");
    return 2;
}

var datasetPath = positionals.ElementAtOrDefault(1)
    ?? Path.Combine(AppContext.BaseDirectory, "tickets.json");

// Optional pacing between tickets. Cloud free tiers (e.g. Groq) enforce a tokens-per-MINUTE (TPM) limit,
// so firing every ticket back-to-back trips HTTP 429. A few seconds between tickets keeps a run under TPM.
//   --delay 30   or   --delay=30
var delaySeconds = ParseDelaySeconds(args);

// --verbose surfaces the per-run "Run … cost — … total_tokens=… latency_ms=…" line (Information level)
// so token usage can be read off for the cost table; otherwise only warnings and the summary are shown.
var verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

// --classify-only runs ONLY the lightweight classification call (category + urgency) and skips the heavy
// 70B drafting/escalation phase. That phase makes several large tool-calling calls per ticket, which trips
// cloud free-tier per-minute token limits; classification is a single small call, so this mode runs fast
// and reliably on a free tier — at the cost of not measuring the escalation decision.
var classifyOnly = args.Contains("--classify-only", StringComparer.OrdinalIgnoreCase);

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

await using var services = BuildServices(provider, verbose);

Console.WriteLine();
Console.WriteLine($"TriageBot eval — provider: {provider}");
Console.WriteLine($"Dataset: {datasetPath} ({cases.Count} cases)");
if (classifyOnly)
    Console.WriteLine("Mode: classify-only (category + urgency; drafting/escalation phase skipped)");
if (delaySeconds > 0)
    Console.WriteLine($"Pacing: {delaySeconds}s between tickets (to stay under the provider's per-minute token limit)");
Console.WriteLine(new string('-', 78));

int categoryHits = 0, urgencyHits = 0, escalateHits = 0, failures = 0;
double totalMs = 0;

for (var i = 0; i < cases.Count; i++)
{
    // Pace requests (except before the first) to respect the provider's tokens-per-minute limit.
    if (delaySeconds > 0 && i > 0)
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

    var c = cases[i];
    using var scope = services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AiProviderState>().Current = provider;

    var db = scope.ServiceProvider.GetRequiredService<TriageBotDbContext>();

    var ticket = new Ticket { Subject = c.Subject, Body = c.Body, RequesterEmail = c.RequesterEmail };
    db.Tickets.Add(ticket);
    await db.SaveChangesAsync();

    var sw = Stopwatch.StartNew();
    bool gotEscalate;
    try
    {
        if (classifyOnly)
        {
            // Classification only: one lightweight call, no 70B drafting/escalation phase.
            var classifier = scope.ServiceProvider.GetRequiredService<TicketClassifier>();
            var run = new AgentRun { TicketId = ticket.Id, Provider = provider.ToString().ToLowerInvariant() };
            db.AgentRuns.Add(run);
            await db.SaveChangesAsync();
            await classifier.ClassifyAndRecordAsync(ticket, provider, new TicketTools(db, run.Id));
            gotEscalate = false; // escalation is not evaluated in this mode
        }
        else
        {
            var triage = scope.ServiceProvider.GetRequiredService<ITicketTriageService>();
            var result = await triage.ProcessTicketAsync(ticket.Id);
            gotEscalate = result?.PendingAction == TicketTools.EscalateToHumanTool;
        }
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

    var catOk = saved.Category == c.ExpectedCategory;
    var urgOk = saved.Urgency == c.ExpectedUrgency;
    var escOk = gotEscalate == c.ShouldEscalate;
    if (catOk) categoryHits++;
    if (urgOk) urgencyHits++;
    if (escOk) escalateHits++;

    var escColumn = classifyOnly
        ? "esc  -  (skipped)"
        : $"esc {Mark(escOk)} {gotEscalate}/{c.ShouldEscalate}";
    Console.WriteLine($"{i + 1,2}. {Trim(c.Subject, 40),-40} " +
        $"cat {Mark(catOk)} {saved.Category?.ToString() ?? "—"}/{c.ExpectedCategory}  " +
        $"urg {Mark(urgOk)} {saved.Urgency?.ToString() ?? "—"}/{c.ExpectedUrgency}  " +
        $"{escColumn}  " +
        $"{sw.Elapsed.TotalMilliseconds,6:F0} ms");
}

var scored = cases.Count - failures;
Console.WriteLine(new string('-', 78));
Console.WriteLine($"Category accuracy:   {categoryHits}/{cases.Count}");
Console.WriteLine($"Urgency accuracy:    {urgencyHits}/{cases.Count}");
Console.WriteLine(classifyOnly
    ? "Escalation accuracy: (skipped — classify-only mode)"
    : $"Escalation accuracy: {escalateHits}/{cases.Count}");
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

// Reads "--delay 30" or "--delay=30" (seconds) from the raw args; 0 (no pacing) if absent/invalid.
static int ParseDelaySeconds(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (a.StartsWith("--delay=", StringComparison.Ordinal)
            && int.TryParse(a["--delay=".Length..], out var inline))
            return Math.Max(0, inline);

        if (a == "--delay" && i + 1 < args.Length && int.TryParse(args[i + 1], out var next))
            return Math.Max(0, next);
    }
    return 0;
}

static ServiceProvider BuildServices(AiProvider provider, bool verbose)
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
    services.AddLogging(b =>
    {
        b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning);
        // On a provider timeout/429 the LLM client AND the triage service each log a large AggregateException
        // stack trace. The eval already reports every failure as a one-line "FAILED: …" (and tallies it in the
        // summary), so those traces are pure noise here — silence both categories by default.
        b.AddFilter("Microsoft.Extensions.AI", LogLevel.None);
        b.AddFilter("TriageBot.Infrastructure", LogLevel.None);
        // In verbose mode, re-enable just the triage service so its per-run cost line (Information) shows.
        // (This also re-enables its failure logging, which verbose users have opted into.)
        if (verbose)
            b.AddFilter("TriageBot.Infrastructure.Agent.TicketTriageService", LogLevel.Information);
    });

    // In-memory database so the eval needs no Postgres; the agent's tools persist here transparently.
    services.AddDbContext<TriageBotDbContext>(o => o.UseInMemoryDatabase("triagebot-eval"));

    services.AddAiProviders(configuration);
    services.AddScoped<TicketClassifier>();
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
