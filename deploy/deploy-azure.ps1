<#
.SYNOPSIS
    Deploys TriageBot to Azure Container Apps by building the image locally and pushing to ACR.

.DESCRIPTION
    Runs the full sequence: resource group -> create ACR -> `docker build` locally -> push the
    image -> `az containerapp up --image ...` (external ingress, port 8080) -> app secrets ->
    env vars (referencing those secrets) -> scale rules (min 0 / max 1) -> sticky sessions
    (for Blazor Server) -> prints the public FQDN.

    We build LOCALLY (not `--source .`) because `az containerapp up --source` builds via ACR
    Tasks (cloud build), which some subscriptions (Free/Trial/Student) block. A local build +
    registry push works everywhere. Requires Docker running locally.

    NO SECRETS ARE HARD-CODED. The Groq key, database connection string and (optional)
    Application Insights connection string are read from parameters or, if omitted, from the
    matching environment variables. They are stored ONLY as Azure Container Apps secrets.

.PARAMETER GroqApiKey
    Groq API key. Falls back to $env:Groq__ApiKey. Required.

.PARAMETER DbConnectionString
    Postgres connection string (Neon URL or Npgsql key-value form). Falls back to
    $env:ConnectionStrings__TriageBotDb. Required.

.PARAMETER AppInsightsConnectionString
    Optional Application Insights connection string. Falls back to
    $env:APPLICATIONINSIGHTS_CONNECTION_STRING. Telemetry is only wired if provided.

.PARAMETER RunMigrationsOnStartup
    If set, adds RunMigrationsOnStartup=true so the container applies EF Core migrations on boot.
    Otherwise run `dotnet ef database update` against the database yourself before/after deploy.

.EXAMPLE
    # From the repo root, after `az login`:
    $env:Groq__ApiKey = "gsk_..."
    $env:ConnectionStrings__TriageBotDb = "postgresql://user:pass@host.neon.tech/neondb?sslmode=require"
    ./deploy/deploy-azure.ps1 -RunMigrationsOnStartup
#>

[CmdletBinding()]
param(
    [string]$ResourceGroup = "triagebot-rg",
    [string]$Location = "southeastasia",
    [string]$AppName = "triagebot",
    [string]$EnvironmentName = "triagebot-env",

    # Secrets — never hard-code; passed in or read from env. (See validation below.)
    [string]$GroqApiKey = $env:Groq__ApiKey,
    [string]$DbConnectionString = $env:ConnectionStrings__TriageBotDb,
    [string]$AppInsightsConnectionString = $env:APPLICATIONINSIGHTS_CONNECTION_STRING,

    # Container registry to push the locally-built image to. If empty, a deterministic,
    # globally-unique-ish name is derived from the subscription + resource group (so re-runs
    # reuse the same registry). ACR names must be 5-50 lowercase alphanumeric characters.
    [string]$AcrName = "",
    [string]$ImageTag = "latest",

    [switch]$RunMigrationsOnStartup
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

# --- Preconditions -----------------------------------------------------------
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI ('az') was not found. Install it: https://aka.ms/azure-cli"
}
# Confirm we're logged in (fails fast with a clear message otherwise).
az account show 1>$null 2>$null
if ($LASTEXITCODE -ne 0) { throw "Not logged in to Azure. Run 'az login' first." }

if ([string]::IsNullOrWhiteSpace($GroqApiKey)) {
    throw "GroqApiKey is required. Pass -GroqApiKey or set `$env:Groq__ApiKey (do NOT commit it)."
}
if ([string]::IsNullOrWhiteSpace($DbConnectionString)) {
    throw "DbConnectionString is required. Pass -DbConnectionString or set `$env:ConnectionStrings__TriageBotDb."
}

# Build context = repo root (this script lives in deploy/).
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Write-Host "Repo root (build context): $RepoRoot"

# --- 1. Resource group -------------------------------------------------------
Write-Step "Creating resource group '$ResourceGroup' in '$Location'"
az group create --name $ResourceGroup --location $Location --output none

# --- 2. Build the image LOCALLY, push to ACR, deploy from the image ----------
# We do NOT use `az containerapp up --source .`: that builds via ACR *Tasks* (cloud build),
# which some subscriptions (Free/Trial/Student) block with 'TasksOperationsNotAllowed'.
# Instead we build with local Docker and push (a normal registry op, always allowed), then
# deploy the pushed image. Make sure `docker build -t triagebot:local .` works locally first.
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker was not found. This script builds the image locally - install Docker Desktop and start it."
}

# Derive a stable, unique-ish ACR name if none was given (so re-runs reuse the same registry).
if ([string]::IsNullOrWhiteSpace($AcrName)) {
    $subId = az account show --query id --output tsv
    $sha = [System.Security.Cryptography.SHA1]::Create().ComputeHash(
        [System.Text.Encoding]::UTF8.GetBytes("$ResourceGroup|$subId"))
    $suffix = -join ($sha[0..5] | ForEach-Object { $_.ToString('x2') })   # 12 hex chars
    $AcrName = "triagebotacr$suffix"
}
$imageRef = "$AcrName.azurecr.io/triagebot:$ImageTag"
Write-Host "Registry: $AcrName.azurecr.io   Image: $imageRef"

Write-Step "Creating/reusing Azure Container Registry '$AcrName'"
az acr create --name $AcrName --resource-group $ResourceGroup --sku Basic --admin-enabled true --output none
if ($LASTEXITCODE -ne 0) { throw "Creating the container registry failed (exit $LASTEXITCODE)." }

Write-Step "Building the image locally with Docker (this can take a few minutes)"
docker build -t $imageRef $RepoRoot
if ($LASTEXITCODE -ne 0) { throw "docker build failed (exit $LASTEXITCODE). Fix it and re-run." }

Write-Step "Pushing the image to ACR"
az acr login --name $AcrName
if ($LASTEXITCODE -ne 0) { throw "az acr login failed (exit $LASTEXITCODE)." }
docker push $imageRef
if ($LASTEXITCODE -ne 0) { throw "docker push failed (exit $LASTEXITCODE)." }

# Create the Container App from the pushed image (external ingress on port 8080).
# NOTE: this first revision has no DB/secret config yet, so it may be unhealthy until step 4
# adds the env vars — that's expected; the final revision is the healthy one.
Write-Step "Creating Container App '$AppName' from the pushed image"
$acrPassword = az acr credential show --name $AcrName --query "passwords[0].value" --output tsv
az containerapp up `
    --name $AppName `
    --resource-group $ResourceGroup `
    --location $Location `
    --environment $EnvironmentName `
    --image $imageRef `
    --ingress external `
    --target-port 8080 `
    --registry-server "$AcrName.azurecr.io" `
    --registry-username $AcrName `
    --registry-password $acrPassword
if ($LASTEXITCODE -ne 0) {
    throw "'az containerapp up' failed (exit $LASTEXITCODE). Fix the error above and re-run; nothing after this step ran."
}

# --- 3. Secrets (the only place secret values live) --------------------------
Write-Step "Setting Container App secrets"
$secretArgs = @(
    "groq-key=$GroqApiKey",
    "db-conn=$DbConnectionString"
)
if (-not [string]::IsNullOrWhiteSpace($AppInsightsConnectionString)) {
    $secretArgs += "appins-conn=$AppInsightsConnectionString"
}
az containerapp secret set --name $AppName --resource-group $ResourceGroup --secrets $secretArgs --output none
if ($LASTEXITCODE -ne 0) { throw "Setting secrets failed (exit $LASTEXITCODE)." }

# --- 4. Environment variables (reference the secrets, never inline values) ----
Write-Step "Setting environment variables (secretref:* points at the secrets above)"
$envArgs = @(
    "Groq__ApiKey=secretref:groq-key",
    "ConnectionStrings__TriageBotDb=secretref:db-conn",
    "Ai__DefaultProvider=Groq"
)
if (-not [string]::IsNullOrWhiteSpace($AppInsightsConnectionString)) {
    $envArgs += "APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:appins-conn"
}
if ($RunMigrationsOnStartup) {
    $envArgs += "RunMigrationsOnStartup=true"
}
az containerapp update --name $AppName --resource-group $ResourceGroup --set-env-vars $envArgs --output none
if ($LASTEXITCODE -ne 0) { throw "Setting environment variables failed (exit $LASTEXITCODE)." }

# --- 5. Scale rules: scale-to-zero, single replica ---------------------------
# min 0  -> no cost while idle (cold start on first request after idle).
# max 1  -> Blazor Server keeps its circuit on one replica (see sticky sessions below).
Write-Step "Setting scale rules (min-replicas 0, max-replicas 1)"
az containerapp update --name $AppName --resource-group $ResourceGroup `
    --min-replicas 0 --max-replicas 1 --output none
if ($LASTEXITCODE -ne 0) { throw "Setting scale rules failed (exit $LASTEXITCODE)." }

# --- 6. Sticky sessions (session affinity) for Blazor Server -----------------
# Blazor Server holds UI state in a per-connection circuit (SignalR). Affinity pins a client
# to the same replica. With max-replicas 1 it's effectively guaranteed, but this makes the
# intent explicit and stays correct if you ever raise max-replicas.
Write-Step "Enabling sticky sessions (affinity = sticky)"
az containerapp ingress sticky-sessions set --name $AppName --resource-group $ResourceGroup `
    --affinity sticky --output none
if ($LASTEXITCODE -ne 0) { throw "Enabling sticky sessions failed (exit $LASTEXITCODE)." }

# --- 7. Output the public URL ------------------------------------------------
Write-Step "Deployment complete"
$fqdn = az containerapp show --name $AppName --resource-group $ResourceGroup `
    --query "properties.configuration.ingress.fqdn" --output tsv
Write-Host "`nApp URL:  https://$fqdn" -ForegroundColor Green
Write-Host "Health:   https://$fqdn/health/ai?provider=groq"
Write-Host "`nTo delete everything and stop all costs:  az group delete --name $ResourceGroup --yes --no-wait`n"
