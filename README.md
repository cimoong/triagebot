# TriageBot

TriageBot is a workflow-automation **agent** for triaging IT support tickets. It reads an incoming
ticket, classifies it (category and priority), drafts a suggested reply, and then either saves the
ticket or escalates it — always pausing for **human approval before any final action is taken**.
The solution follows a simple Clean Architecture layout (Web · Core · Infrastructure · Tests) so the
agent, persistence, and LLM integration can evolve independently. This README is a placeholder and
will be expanded as the project grows.

## Solution layout

| Project                    | Responsibility                                                            |
| -------------------------- | ------------------------------------------------------------------------- |
| `TriageBot.Web`            | ASP.NET Core + Blazor Server host: UI, API endpoints, composition root.   |
| `TriageBot.Core`           | Domain models, enums, tool & service abstractions (no external deps).     |
| `TriageBot.Infrastructure` | EF Core / LLM-agent / tool implementations behind the Core interfaces.    |
| `TriageBot.Tests`          | xUnit unit tests for tools and services.                                  |

## Getting started

```bash
# Restore & build the whole solution
dotnet build

# Run the unit tests
dotnet test

# Run the web app (from the repo root)
dotnet run --project src/TriageBot.Web
```

## Database (Docker)

A local PostgreSQL instance is provided via `docker-compose.yml`.

| Setting   | Value       |
| --------- | ----------- |
| Host/Port | `localhost:5433` (mapped to the container's 5432) |
| Database  | `triagebot` |
| User      | `postgres`  |
| Password  | `postgres`  |

> Host port **5433** is used to avoid clashing with a native PostgreSQL install that may already
> own 5432. Change the published port in `docker-compose.yml` and the `TriageBotDb` connection
> string together if you prefer a different one.

```bash
# Start the database in the background
docker compose up -d

# Check status / wait for the "healthy" state
docker compose ps

# Tail the logs
docker compose logs -f db

# Stop the container (keeps data in the named volume)
docker compose down

# Stop and DELETE all data (drops the named volume)
docker compose down -v
```

Connection string (configured in `src/TriageBot.Web/appsettings.json` under `ConnectionStrings:TriageBotDb`):

```
Host=localhost;Port=5433;Database=triagebot;Username=postgres;Password=postgres
```

### Apply EF Core migrations

```bash
# Create the schema and seed sample tickets
dotnet ef database update --project src/TriageBot.Infrastructure --startup-project src/TriageBot.Web
```

> Note: AI/LLM and agent-runtime dependencies are intentionally not wired in yet.
> The current `Infrastructure` layer ships deterministic placeholder implementations
> (keyword classifier, in-memory repository) alongside the EF Core data layer.
