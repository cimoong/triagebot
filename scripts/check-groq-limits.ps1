<#
.SYNOPSIS
    Checks current Groq rate-limit status (tokens/requests remaining) for the classify (8B) and
    draft (70B) models, without spending real usage against the app's normal flow.

.DESCRIPTION
    Sends one minimal chat completion (max_tokens=1) per model and reads Groq's x-ratelimit-*
    response headers. Useful before running the eval or a manual UI test, to confirm the
    tokens-per-minute (TPM) bucket has room.

    IMPORTANT: these headers only report the per-MINUTE bucket. Groq's free tier also enforces a
    separate tokens-per-DAY (TPD) cap that is NOT exposed via these headers — a "healthy" TPM
    reading here does not guarantee a real request will succeed if the daily cap is close to its
    limit. TPD exhaustion only surfaces when an actual request 429s; read the response body, e.g.:
      "Rate limit reached ... on tokens per day (TPD): Limit 100000, Used 99876, ... try again in 29m18s"
    That message tells you the real remaining wait — it is usually far less than 24h (Groq's daily
    cap is a rolling window, not a calendar-day reset), but this script cannot predict it in advance.

    The API key is read from user-secrets (Groq:ApiKey, shared with the web app) so it is never
    typed on the command line or committed. Falls back to $env:Groq__ApiKey if user-secrets has
    no value.

.EXAMPLE
    ./scripts/check-groq-limits.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Get-GroqApiKey {
    $line = dotnet user-secrets list --project (Join-Path $RepoRoot "src/TriageBot.Web") 2>$null |
        Select-String '^Groq:ApiKey\s*='
    if ($line) {
        return ($line.ToString() -split '=', 2)[1].Trim()
    }
    if ($env:Groq__ApiKey) {
        return $env:Groq__ApiKey
    }
    throw "No Groq API key found. Set it via 'dotnet user-secrets set Groq:ApiKey ...' (from src/TriageBot.Web) or `$env:Groq__ApiKey."
}

function Test-GroqModel([string]$Model, [string]$ApiKey) {
    $body = @{
        model      = $Model
        messages   = @(@{ role = "user"; content = "x" })
        max_tokens = 1
    } | ConvertTo-Json -Compress

    $headers = @{
        "Authorization" = "Bearer $ApiKey"
        "Content-Type"  = "application/json"
    }

    try {
        $response = Invoke-WebRequest -Uri "https://api.groq.com/openai/v1/chat/completions" `
            -Method Post -Headers $headers -Body $body -TimeoutSec 20 -ErrorAction Stop

        [pscustomobject]@{
            Model              = $Model
            Status             = [int]$response.StatusCode
            RemainingRequests  = $response.Headers["x-ratelimit-remaining-requests"]
            LimitRequests      = $response.Headers["x-ratelimit-limit-requests"]
            RemainingTokens    = $response.Headers["x-ratelimit-remaining-tokens"]
            LimitTokens        = $response.Headers["x-ratelimit-limit-tokens"]
            ResetTokens        = $response.Headers["x-ratelimit-reset-tokens"]
        }
    } catch {
        $resp = $_.Exception.Response
        $status = if ($resp) { [int]$resp.StatusCode } else { "N/A" }
        $retryAfter = if ($resp) { $resp.Headers["Retry-After"] } else { $null }
        [pscustomobject]@{
            Model              = $Model
            Status             = "$status (FAILED)"
            RemainingRequests  = "-"
            LimitRequests      = "-"
            RemainingTokens    = "-"
            LimitTokens        = "-"
            ResetTokens        = if ($retryAfter) { "Retry-After: $retryAfter" } else { $_.Exception.Message }
        }
    }
}

$apiKey = Get-GroqApiKey
Write-Host "Checking Groq rate-limit status..." -ForegroundColor Cyan

$results = @(
    Test-GroqModel -Model "llama-3.1-8b-instant" -ApiKey $apiKey       # classification model
    Test-GroqModel -Model "llama-3.3-70b-versatile" -ApiKey $apiKey    # drafting model
)

$results | Format-Table -AutoSize

$lowTokenBucket = $results | Where-Object {
    $_.RemainingTokens -match '^\d+$' -and $_.LimitTokens -match '^\d+$' -and
    ([int]$_.RemainingTokens) -lt ([int]$_.LimitTokens * 0.5)
}
if ($lowTokenBucket) {
    Write-Host "`nWarning: token bucket below 50% for: $($lowTokenBucket.Model -join ', ')" -ForegroundColor Yellow
    Write-Host "Wait a bit (bucket refills every ~1 minute) before running the eval or a batch test." -ForegroundColor Yellow
} else {
    Write-Host "`nToken buckets look healthy." -ForegroundColor Green
}
