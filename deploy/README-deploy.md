# Deploying TriageBot to Azure Container Apps

This guide deploys the containerized app to **Azure Container Apps (ACA)** — a serverless
container platform that can **scale to zero** (no cost while idle).

The script **builds the image locally with Docker and pushes it to Azure Container Registry
(ACR)**, then deploys that image. It deliberately does *not* use `az containerapp up --source .`,
because that path builds via **ACR Tasks** (cloud build), which some subscriptions
(Free/Trial/Student) block with `TasksOperationsNotAllowed`. A local build + push works on any
subscription.

> **Before you deploy:** confirm the container builds and runs locally first (see the main
> README, *"Run the production image locally"*). A successful `docker build -t triagebot:local .`
> and a working `docker compose -f docker-compose.prod.yml up --build` are the pre-flight check.
> **Docker must be running** when you run the deploy script.

## Prerequisites

- [Azure CLI](https://aka.ms/azure-cli) (`az`) and an Azure subscription.
- The Container Apps CLI extension, and the resource providers registered **once per
  subscription** (registration takes a minute or two — wait until each shows `Registered`).
  `Microsoft.ContainerRegistry` is required because `containerapp up` builds the image via ACR:
  ```bash
  az extension add --name containerapp --upgrade
  az provider register --namespace Microsoft.App
  az provider register --namespace Microsoft.OperationalInsights
  az provider register --namespace Microsoft.ContainerRegistry
  # check readiness:
  az provider show --namespace Microsoft.ContainerRegistry --query registrationState -o tsv
  ```
- **Docker Desktop running** (the script builds the image locally and pushes it).
- A reachable Postgres (e.g. **Neon** — see the main README, *"Production database (Neon)"*).
- A **Groq API key**.

## Security first

- **Never commit real secret values.** The Groq key, database connection string and any
  Application Insights connection string are passed in at deploy time and stored **only** as
  **Azure Container Apps secrets**. Environment variables reference them via `secretref:<name>`,
  so the plaintext never appears in the app's env config, in this repo, or in the image.
- `deploy/.env.example` holds placeholders only; your real `.env` is git-ignored.
- If a secret ever leaks, rotate it at the source (Groq / Neon) and re-run the secret step.

## One-command deploy (recommended)

From the **repo root**, after `az login`:

```powershell
# Provide secrets via env (they are NOT hard-coded anywhere):
$env:Groq__ApiKey = "gsk_your_groq_key"
$env:ConnectionStrings__TriageBotDb = "postgresql://user:pass@host.neon.tech/neondb?sslmode=require"
# Optional APM:
# $env:APPLICATIONINSIGHTS_CONNECTION_STRING = "InstrumentationKey=...;IngestionEndpoint=..."

# Run the script (add -RunMigrationsOnStartup to let the container create the schema on boot):
./deploy/deploy-azure.ps1 -RunMigrationsOnStartup
```

The script prints the public URL at the end, e.g. `https://triagebot.<hash>.<region>.azurecontainerapps.io`.

## What each step does

| Step | Command | Purpose |
| ---- | ------- | ------- |
| 1 | `az group create` | Creates the resource group that holds everything (easy teardown). |
| 2a | `az acr create --sku Basic --admin-enabled` | Creates an Azure Container Registry to hold the image. |
| 2b | `docker build` + `az acr login` + `docker push` | Builds the image **locally** and pushes it to ACR (no ACR Tasks / cloud build). |
| 2c | `az containerapp up --image <acr>/triagebot:latest --ingress external --target-port 8080` | Creates the Container Apps environment (if needed) and deploys the pushed image with a public HTTPS endpoint on container port **8080**. |
| 3 | `az containerapp secret set --secrets groq-key=… db-conn=… appins-conn=…` | Stores secret values as ACA secrets (encrypted, not shown in env config). |
| 4 | `az containerapp update --set-env-vars …=secretref:…` | Sets env vars that **reference** the secrets (`Groq__ApiKey`, `ConnectionStrings__TriageBotDb`, `Ai__DefaultProvider=Groq`, optional App Insights). ASP.NET Core reads these via the `__` separator. This creates a new, healthy revision. |
| 5 | `az containerapp update --min-replicas 0 --max-replicas 1` | Scale rules: idle to **zero**, cap at **one** replica. |
| 6 | `az containerapp ingress sticky-sessions set --affinity sticky` | Session affinity for Blazor Server (see below). |
| 7 | `az containerapp show --query …ingress.fqdn` | Prints the public FQDN. |

> **Why secrets are set *after* `up`:** the first revision from step 2 has no DB config yet, so
> it may be unhealthy until step 4 adds the env vars. That's expected — the revision created in
> step 4 is the one that serves traffic.

## Blazor Server notes (important)

TriageBot's UI is **Blazor Server**: each browser session holds server-side state in a
**circuit** (a persistent SignalR connection). Two consequences on ACA:

- **Sticky sessions (`--affinity sticky`)** keep a client pinned to the same replica so its
  circuit stays intact. With **`--max-replicas 1`** there is only one replica, so affinity is
  effectively guaranteed — but we set it explicitly so the intent is clear and it remains correct
  if you raise `max-replicas` later. (Scaling Blazor Server across many replicas also needs a
  backplane; out of scope for this demo.)
- **`--min-replicas 0` (scale-to-zero)** means **no cost while idle**, but the first request after
  an idle period triggers a **cold start** (a few seconds), and any in-flight circuits are lost
  when the app scales down. For an always-warm demo, set `--min-replicas 1` instead — at the cost
  of running continuously. This pairs with the app's built-in DB retry, which absorbs the
  serverless-Postgres cold start on that first request.

## Verify

1. Open `https://<fqdn>/` → **Tickets**.
2. `https://<fqdn>/health/ai?provider=groq` should return `200` with `{"reply":"OK"}`.
3. Add a ticket → **Process with agent** → the timeline shows tool calls → approve/reject.

## Tear down (stop all costs → zero)

Deleting the resource group removes the Container App, its environment, the ACR build
artifacts, and logs — everything created above:

```bash
az group delete --name triagebot-rg --yes --no-wait
```

> Scale-to-zero already means ~no compute cost while idle, but the Container Apps environment,
> ACR, and Log Analytics can incur small standing charges. Deleting the resource group is the
> only way to guarantee **zero** ongoing cost.

## Updating an existing deployment

Re-run the script (it's idempotent) or just rebuild/redeploy the image:

```bash
az containerapp up --name triagebot --resource-group triagebot-rg --source .
```

Secrets and env vars persist across `up` runs, so you don't need to re-set them unless they change.

## Optional: CI/CD with GitHub Actions

See [`azure-deploy.yml`](azure-deploy.yml) for an **optional** workflow that builds and deploys on
push to `production` using Azure OIDC login. It is disabled by default (guarded so it only runs
when the required repo secrets exist) and every value is a placeholder — read its comments before
enabling.
