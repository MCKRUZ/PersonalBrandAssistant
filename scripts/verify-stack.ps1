#Requires -Version 7.0
<#
.SYNOPSIS
    End-to-end Docker Compose stack verification for Personal Brand Assistant.
.DESCRIPTION
    Builds and starts the full stack, then runs integration checks:
    1. API health endpoint
    2. Content CRUD round-trip
    3. Angular app loads
    4. Data persists across container restart
#>

[CmdletBinding()]
param(
    [string]$ApiUrl = "http://localhost:5000",
    [string]$WebUrl = "http://localhost:4200",
    [string]$ApiKey,
    [int]$MaxWait = 120
)

$ErrorActionPreference = "Stop"
$PassCount = 0
$FailCount = 0
$ContentId = $null

function Write-Pass($Message) {
    Write-Host "[PASS] $Message" -ForegroundColor Green
    $script:PassCount++
}

function Write-Fail($Message) {
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    $script:FailCount++
}

function Write-Info($Message) {
    Write-Host "[INFO] $Message" -ForegroundColor Yellow
}

function Resolve-ApiKey {
    if ($script:ApiKey) { return }

    if ($env:API_KEY) {
        $script:ApiKey = $env:API_KEY
        return
    }

    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
    $envFile = Join-Path $ProjectRoot ".env"
    if (Test-Path $envFile) {
        $envLine = Select-String -Path $envFile -Pattern "^API_KEY=" | Select-Object -First 1
        if ($envLine) {
            $script:ApiKey = ($envLine.Line -split '=', 2)[1]
        }
    }

    if (-not $script:ApiKey) {
        Write-Host "ERROR: ApiKey parameter, API_KEY env var, or API_KEY in .env is required." -ForegroundColor Red
        exit 1
    }
}

function Test-PortAvailable {
    $conflict = $false
    foreach ($port in @(5000, 5432, 4200)) {
        $inUse = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
        if ($inUse) {
            Write-Host "ERROR: Port $port is already in use" -ForegroundColor Red
            $conflict = $true
        }
    }
    if ($conflict) { exit 1 }
}

function Wait-ForHealthy {
    param(
        [string]$Url,
        [string]$Label,
        [int]$Timeout = $MaxWait
    )

    Write-Info "Waiting for $Label to become healthy ($Url)..."
    $elapsed = 0
    $delay = 2

    while ($elapsed -lt $Timeout) {
        try {
            $response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 5 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Info "$Label is healthy (${elapsed}s)"
                return $true
            }
        }
        catch { }

        Start-Sleep -Seconds $delay
        $elapsed += $delay
        if ($delay -lt 16) { $delay *= 2 }
    }

    Write-Host "ERROR: $Label did not become healthy within ${Timeout}s" -ForegroundColor Red
    return $false
}

function Invoke-Cleanup {
    if ($script:ContentId) {
        try {
            Invoke-WebRequest -Uri "$ApiUrl/api/content/$($script:ContentId)" -Method Delete `
                -Headers @{ "X-Api-Key" = $script:ApiKey } -UseBasicParsing -ErrorAction SilentlyContinue | Out-Null
        } catch { }
    }
    Write-Info "Tearing down stack..."
    docker compose down -v 2>$null
}

# Prerequisites
if (-not (Get-Command "docker" -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: docker is required but not found on PATH" -ForegroundColor Red
    exit 1
}

try {
    docker compose version | Out-Null
}
catch {
    Write-Host "ERROR: docker compose is required but not available" -ForegroundColor Red
    exit 1
}

Resolve-ApiKey

# Setup
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
Push-Location $ProjectRoot

try {
    if (-not (Test-Path ".env")) {
        Write-Info "Creating .env from .env.example..."
        Copy-Item ".env.example" ".env"
    }

    Test-PortAvailable

    # Build and start
    Write-Info "Building and starting Docker Compose stack..."
    docker compose up -d --build

    # Test 1: Health check
    if (Wait-ForHealthy -Url "$ApiUrl/health" -Label "API") {
        try {
            $health = Invoke-WebRequest -Uri "$ApiUrl/health" -UseBasicParsing
            if ($health.StatusCode -eq 200) {
                Write-Pass "Stack starts and API health returns 200"
            }
            else {
                Write-Fail "API health returned $($health.StatusCode), expected 200"
            }
        }
        catch {
            Write-Fail "API health endpoint error: $_"
        }
    }
    else {
        Write-Fail "API health endpoint did not respond within ${MaxWait}s"
    }

    # Wait for Angular
    Wait-ForHealthy -Url $WebUrl -Label "Angular" -Timeout 60 | Out-Null

    # Test 2: CRUD round-trip
    Write-Info "Testing content CRUD round-trip..."
    $headers = @{
        "Content-Type" = "application/json"
        "X-Api-Key"    = $ApiKey
    }
    $createBody = @{
        contentType = "BlogPost"
        title       = "Verification Test Post"
        body        = "This is an end-to-end verification test."
    } | ConvertTo-Json

    try {
        $createResponse = Invoke-WebRequest -Uri "$ApiUrl/api/content" -Method Post -Headers $headers -Body $createBody -UseBasicParsing
        if ($createResponse.StatusCode -ne 201) {
            Write-Fail "Create content returned $($createResponse.StatusCode), expected 201"
        }
        else {
            $createData = $createResponse.Content | ConvertFrom-Json
            $ContentId = $createData.id ?? $createData.Id

            if ($ContentId) {
                $readResponse = Invoke-RestMethod -Uri "$ApiUrl/api/content/$ContentId" -Headers @{ "X-Api-Key" = $ApiKey }

                $title = $readResponse.title ?? $readResponse.Title
                $body = $readResponse.body ?? $readResponse.Body
                $status = $readResponse.status ?? $readResponse.Status
                $contentType = $readResponse.contentType ?? $readResponse.ContentType

                if ($title -eq "Verification Test Post" -and
                    $body -eq "This is an end-to-end verification test." -and
                    $status -eq "Draft" -and
                    $contentType -eq "BlogPost") {
                    Write-Pass "Create and read content round-trip"
                }
                else {
                    Write-Fail "Content fields mismatch -- title='$title', body='$body', status='$status', contentType='$contentType'"
                }
            }
            else {
                Write-Fail "Create response missing content ID"
            }
        }
    }
    catch {
        Write-Fail "CRUD round-trip error: $_"
    }

    # Test 3: Angular app loads
    Write-Info "Testing Angular app..."
    try {
        $webResponse = Invoke-WebRequest -Uri $WebUrl -UseBasicParsing
        if ($webResponse.StatusCode -eq 200 -and $webResponse.Content -match "app-root") {
            Write-Pass "Angular app loads and contains app-root"
        }
        elseif ($webResponse.StatusCode -eq 200) {
            Write-Fail "Angular app returned 200 but missing app-root element"
        }
        else {
            Write-Fail "Angular app returned $($webResponse.StatusCode), expected 200"
        }
    }
    catch {
        Write-Fail "Angular app error: $_"
    }

    # Test 4: Persistence across restart
    if ($ContentId) {
        Write-Info "Restarting api and db services..."
        docker compose restart api db

        if (Wait-ForHealthy -Url "$ApiUrl/health" -Label "API after restart" -Timeout 60) {
            try {
                $persistResponse = Invoke-RestMethod -Uri "$ApiUrl/api/content/$ContentId" -Headers @{ "X-Api-Key" = $ApiKey }
                $title = $persistResponse.title ?? $persistResponse.Title

                if ($title -eq "Verification Test Post") {
                    Write-Pass "Data persists across container restart"
                }
                else {
                    Write-Fail "Data changed after restart -- title='$title'"
                }
            }
            catch {
                Write-Fail "Persistence check error: $_"
            }
        }
        else {
            Write-Fail "API did not recover after restart"
        }
    }
    else {
        Write-Fail "Skipping persistence test -- no content ID from Test 2"
    }

    # Summary
    Write-Host ""
    Write-Host "================================"
    Write-Host "  Verification Summary"
    Write-Host "================================"
    Write-Host "  Passed: $PassCount" -ForegroundColor Green
    Write-Host "  Failed: $FailCount" -ForegroundColor Red
    Write-Host "================================"

    if ($FailCount -gt 0) {
        Write-Info "Dumping logs for failed services..."
        docker compose logs --tail=50 api 2>$null
        docker compose logs --tail=50 db 2>$null
        docker compose logs --tail=50 web 2>$null
    }
}
finally {
    Invoke-Cleanup
    Pop-Location
}

exit ([math]::Min($FailCount, 1))
