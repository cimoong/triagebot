<#
.SYNOPSIS
    Deletes the Azure resource group created by deploy-azure.ps1, stopping all Azure charges.

.DESCRIPTION
    Deletes the resource group (Container App, its environment, and the Container Registry all
    live inside it) in one shot. This does NOT touch Neon (separate service — delete the project
    from the Neon console) or Groq (no resource to delete, just stop using the key).

    Runs interactively by default (asks for confirmation). Pass -Force to skip the prompt (e.g.
    for a script/CI context). Deletion runs in the background on Azure's side (--no-wait), so this
    command returns quickly; the resource group finishes disappearing a few minutes later.

.PARAMETER ResourceGroup
    Must match what you deployed with (deploy-azure.ps1 default: "triagebot-rg").

.EXAMPLE
    ./deploy/teardown-azure.ps1

.EXAMPLE
    ./deploy/teardown-azure.ps1 -ResourceGroup triagebot-rg -Force
#>

[CmdletBinding()]
param(
    [string]$ResourceGroup = "triagebot-rg",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI ('az') was not found. Install it: https://aka.ms/azure-cli"
}
az account show 1>$null 2>$null
if ($LASTEXITCODE -ne 0) { throw "Not logged in to Azure. Run 'az login' first." }

$exists = az group exists --name $ResourceGroup
if ($exists -ne "true") {
    Write-Host "Resource group '$ResourceGroup' does not exist (already deleted?). Nothing to do." -ForegroundColor Yellow
    exit 0
}

Write-Host "This deletes resource group '$ResourceGroup' and EVERYTHING in it:" -ForegroundColor Yellow
az resource list --resource-group $ResourceGroup --query "[].{Name:name, Type:type}" --output table

if (-not $Force) {
    $confirm = Read-Host "`nType the resource group name to confirm deletion"
    if ($confirm -ne $ResourceGroup) {
        Write-Host "Confirmation did not match. Aborted — nothing deleted." -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "`n==> Deleting resource group '$ResourceGroup' (running in the background)..." -ForegroundColor Cyan
az group delete --name $ResourceGroup --yes --no-wait
if ($LASTEXITCODE -ne 0) { throw "az group delete failed (exit $LASTEXITCODE)." }

Write-Host "Deletion started. It finishes in the background (check: az group exists --name $ResourceGroup)." -ForegroundColor Green
Write-Host "`nReminder — this only covers Azure. Also stop cost/exposure elsewhere:" -ForegroundColor Yellow
Write-Host "  - Neon:  delete the project from https://console.neon.tech (separate service, not touched above)."
Write-Host "  - Groq:  no resource to delete; just stop using / rotate the API key if you're done."
