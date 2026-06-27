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
| Host/Port | `localhost:5432` |
| Database  | `triagebot` |
| User      | `postgres`  |
| Password  | `postgres`  |

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

Connection string (for later EF Core wiring):

```
Host=localhost;Port=5432;Database=triagebot;Username=postgres;Password=postgres
```

> Note: AI/LLM and agent-runtime dependencies are intentionally not wired in yet.
> The current `Infrastructure` layer ships deterministic placeholder implementations
> (keyword classifier, in-memory repository) so the workflow runs end-to-end today.
