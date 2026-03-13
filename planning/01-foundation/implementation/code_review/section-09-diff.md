diff --git a/scripts/verify-stack.ps1 b/scripts/verify-stack.ps1
new file mode 100644
index 0000000..db4056c
--- /dev/null
+++ b/scripts/verify-stack.ps1
@@ -0,0 +1,234 @@
+#Requires -Version 7.0
+<#
+.SYNOPSIS
+    End-to-end Docker Compose stack verification for Personal Brand Assistant.
+.DESCRIPTION
+    Builds and starts the full stack, then runs integration checks:
+    1. API health endpoint
+    2. Content CRUD round-trip
+    3. Angular app loads
+    4. Data persists across container restart
+#>
+
+[CmdletBinding()]
+param(
+    [string]$ApiUrl = "http://localhost:5000",
+    [string]$WebUrl = "http://localhost:4200",
+    [string]$ApiKey = $env:API_KEY ?? "ChangeMeInProduction123!",
+    [int]$MaxWait = 120
+)
+
+$ErrorActionPreference = "Stop"
+$PassCount = 0
+$FailCount = 0
+$ContentId = $null
+
+function Write-Pass($Message) {
+    Write-Host "[PASS] $Message" -ForegroundColor Green
+    $script:PassCount++
+}
+
+function Write-Fail($Message) {
+    Write-Host "[FAIL] $Message" -ForegroundColor Red
+    $script:FailCount++
+}
+
+function Write-Info($Message) {
+    Write-Host "[INFO] $Message" -ForegroundColor Yellow
+}
+
+function Wait-ForHealthy {
+    param(
+        [string]$Url,
+        [string]$Label,
+        [int]$Timeout = $MaxWait
+    )
+
+    Write-Info "Waiting for $Label to become healthy ($Url)..."
+    $elapsed = 0
+    $delay = 2
+
+    while ($elapsed -lt $Timeout) {
+        try {
+            $response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 5 -ErrorAction SilentlyContinue
+            if ($response.StatusCode -eq 200) {
+                Write-Info "$Label is healthy (${elapsed}s)"
+                return $true
+            }
+        }
+        catch { }
+
+        Start-Sleep -Seconds $delay
+        $elapsed += $delay
+        if ($delay -lt 10) { $delay += 2 }
+    }
+
+    Write-Host "ERROR: $Label did not become healthy within ${Timeout}s" -ForegroundColor Red
+    return $false
+}
+
+function Invoke-Cleanup {
+    Write-Info "Tearing down stack..."
+    docker compose down -v 2>$null
+}
+
+# Prerequisites
+foreach ($cmd in @("docker", "curl")) {
+    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
+        Write-Host "ERROR: $cmd is required but not found on PATH" -ForegroundColor Red
+        exit 1
+    }
+}
+
+try {
+    docker compose version | Out-Null
+}
+catch {
+    Write-Host "ERROR: docker compose is required but not available" -ForegroundColor Red
+    exit 1
+}
+
+# Setup
+$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
+Push-Location $ProjectRoot
+
+try {
+    if (-not (Test-Path ".env")) {
+        Write-Info "Creating .env from .env.example..."
+        Copy-Item ".env.example" ".env"
+    }
+
+    # Build and start
+    Write-Info "Building and starting Docker Compose stack..."
+    docker compose up -d --build
+
+    # Test 1: Health check
+    if (Wait-ForHealthy -Url "$ApiUrl/health" -Label "API") {
+        try {
+            $health = Invoke-WebRequest -Uri "$ApiUrl/health" -UseBasicParsing
+            if ($health.StatusCode -eq 200) {
+                Write-Pass "Stack starts and API health returns 200"
+            }
+            else {
+                Write-Fail "API health returned $($health.StatusCode), expected 200"
+            }
+        }
+        catch {
+            Write-Fail "API health endpoint error: $_"
+        }
+    }
+    else {
+        Write-Fail "API health endpoint did not respond within ${MaxWait}s"
+    }
+
+    # Wait for Angular
+    Wait-ForHealthy -Url $WebUrl -Label "Angular" -Timeout 60 | Out-Null
+
+    # Test 2: CRUD round-trip
+    Write-Info "Testing content CRUD round-trip..."
+    $headers = @{
+        "Content-Type" = "application/json"
+        "X-Api-Key"    = $ApiKey
+    }
+    $createBody = @{
+        contentType = "BlogPost"
+        title       = "Verification Test Post"
+        body        = "This is an end-to-end verification test."
+    } | ConvertTo-Json
+
+    try {
+        $createResponse = Invoke-RestMethod -Uri "$ApiUrl/api/content" -Method Post -Headers $headers -Body $createBody
+        $ContentId = $createResponse.id ?? $createResponse.Id
+
+        if ($ContentId) {
+            $readResponse = Invoke-RestMethod -Uri "$ApiUrl/api/content/$ContentId" -Headers @{ "X-Api-Key" = $ApiKey }
+
+            $title = $readResponse.title ?? $readResponse.Title
+            $body = $readResponse.body ?? $readResponse.Body
+            $status = $readResponse.status ?? $readResponse.Status
+
+            if ($title -eq "Verification Test Post" -and
+                $body -eq "This is an end-to-end verification test." -and
+                $status -eq "Draft") {
+                Write-Pass "Create and read content round-trip"
+            }
+            else {
+                Write-Fail "Content fields mismatch -- title='$title', body='$body', status='$status'"
+            }
+        }
+        else {
+            Write-Fail "Create response missing content ID"
+        }
+    }
+    catch {
+        Write-Fail "CRUD round-trip error: $_"
+    }
+
+    # Test 3: Angular app loads
+    Write-Info "Testing Angular app..."
+    try {
+        $webResponse = Invoke-WebRequest -Uri $WebUrl -UseBasicParsing
+        if ($webResponse.StatusCode -eq 200 -and $webResponse.Content -match "app-root") {
+            Write-Pass "Angular app loads and contains app-root"
+        }
+        elseif ($webResponse.StatusCode -eq 200) {
+            Write-Fail "Angular app returned 200 but missing app-root element"
+        }
+        else {
+            Write-Fail "Angular app returned $($webResponse.StatusCode), expected 200"
+        }
+    }
+    catch {
+        Write-Fail "Angular app error: $_"
+    }
+
+    # Test 4: Persistence across restart
+    if ($ContentId) {
+        Write-Info "Restarting api and db services..."
+        docker compose restart api db
+
+        if (Wait-ForHealthy -Url "$ApiUrl/health" -Label "API after restart" -Timeout 60) {
+            try {
+                $persistResponse = Invoke-RestMethod -Uri "$ApiUrl/api/content/$ContentId" -Headers @{ "X-Api-Key" = $ApiKey }
+                $title = $persistResponse.title ?? $persistResponse.Title
+
+                if ($title -eq "Verification Test Post") {
+                    Write-Pass "Data persists across container restart"
+                }
+                else {
+                    Write-Fail "Data changed after restart -- title='$title'"
+                }
+            }
+            catch {
+                Write-Fail "Persistence check error: $_"
+            }
+        }
+        else {
+            Write-Fail "API did not recover after restart"
+        }
+    }
+    else {
+        Write-Fail "Skipping persistence test -- no content ID from Test 2"
+    }
+
+    # Summary
+    Write-Host ""
+    Write-Host "================================"
+    Write-Host "  Verification Summary"
+    Write-Host "================================"
+    Write-Host "  Passed: $PassCount" -ForegroundColor Green
+    Write-Host "  Failed: $FailCount" -ForegroundColor Red
+    Write-Host "================================"
+
+    if ($FailCount -gt 0) {
+        Write-Info "Dumping logs for failed services..."
+        docker compose logs --tail=50 api 2>$null
+        docker compose logs --tail=50 db 2>$null
+    }
+}
+finally {
+    Invoke-Cleanup
+    Pop-Location
+}
+
+exit $FailCount
diff --git a/scripts/verify-stack.sh b/scripts/verify-stack.sh
new file mode 100644
index 0000000..535975f
--- /dev/null
+++ b/scripts/verify-stack.sh
@@ -0,0 +1,231 @@
+#!/usr/bin/env bash
+set -euo pipefail
+
+# Configuration
+API_URL="http://localhost:5000"
+WEB_URL="http://localhost:4200"
+API_KEY="${API_KEY:-ChangeMeInProduction123!}"
+MAX_WAIT=120
+PASS_COUNT=0
+FAIL_COUNT=0
+CONTENT_ID=""
+
+# Colors
+RED='\033[0;31m'
+GREEN='\033[0;32m'
+YELLOW='\033[1;33m'
+NC='\033[0m'
+
+pass() {
+    echo -e "${GREEN}[PASS]${NC} $1"
+    ((PASS_COUNT++))
+}
+
+fail() {
+    echo -e "${RED}[FAIL]${NC} $1"
+    ((FAIL_COUNT++))
+}
+
+info() {
+    echo -e "${YELLOW}[INFO]${NC} $1"
+}
+
+cleanup() {
+    info "Tearing down stack..."
+    docker compose down -v 2>/dev/null || true
+}
+
+trap cleanup EXIT
+
+# Prerequisites
+check_prerequisites() {
+    local missing=0
+    for cmd in docker curl jq; do
+        if ! command -v "$cmd" &>/dev/null; then
+            echo "ERROR: $cmd is required but not found on PATH"
+            missing=1
+        fi
+    done
+
+    if ! docker compose version &>/dev/null; then
+        echo "ERROR: docker compose is required but not available"
+        missing=1
+    fi
+
+    if [ "$missing" -eq 1 ]; then
+        exit 1
+    fi
+}
+
+wait_for_healthy() {
+    local url="$1"
+    local label="$2"
+    local max_wait="${3:-$MAX_WAIT}"
+    local elapsed=0
+    local delay=2
+
+    info "Waiting for $label to become healthy ($url)..."
+
+    while [ "$elapsed" -lt "$max_wait" ]; do
+        local status
+        status=$(curl -s -o /dev/null -w '%{http_code}' "$url" 2>/dev/null || echo "000")
+        if [ "$status" = "200" ]; then
+            info "$label is healthy (${elapsed}s)"
+            return 0
+        fi
+        sleep "$delay"
+        elapsed=$((elapsed + delay))
+        if [ "$delay" -lt 10 ]; then
+            delay=$((delay + 2))
+        fi
+    done
+
+    echo "ERROR: $label did not become healthy within ${max_wait}s (last status: $status)"
+    return 1
+}
+
+# Setup
+check_prerequisites
+
+SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
+PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
+cd "$PROJECT_ROOT"
+
+if [ ! -f ".env" ]; then
+    info "Creating .env from .env.example..."
+    cp .env.example .env
+fi
+
+# Build and start
+info "Building and starting Docker Compose stack..."
+docker compose up -d --build
+
+# Test 1: Health check
+if wait_for_healthy "$API_URL/health" "API"; then
+    status=$(curl -s -o /dev/null -w '%{http_code}' "$API_URL/health")
+    if [ "$status" = "200" ]; then
+        pass "Stack starts and API health returns 200"
+    else
+        fail "API health returned $status, expected 200"
+    fi
+else
+    fail "API health endpoint did not respond within ${MAX_WAIT}s"
+fi
+
+# Wait for Angular
+wait_for_healthy "$WEB_URL" "Angular" 60 || true
+
+# Test 2: CRUD round-trip
+info "Testing content CRUD round-trip..."
+create_response=$(curl -s -w '\n%{http_code}' \
+    -X POST "$API_URL/api/content" \
+    -H "Content-Type: application/json" \
+    -H "X-Api-Key: $API_KEY" \
+    -d '{
+        "contentType": "BlogPost",
+        "title": "Verification Test Post",
+        "body": "This is an end-to-end verification test."
+    }')
+
+create_status=$(echo "$create_response" | tail -1)
+create_body=$(echo "$create_response" | sed '$d')
+
+if [ "$create_status" = "201" ]; then
+    CONTENT_ID=$(echo "$create_body" | jq -r '.id // .Id // empty')
+
+    if [ -n "$CONTENT_ID" ]; then
+        read_response=$(curl -s -w '\n%{http_code}' \
+            "$API_URL/api/content/$CONTENT_ID" \
+            -H "X-Api-Key: $API_KEY")
+
+        read_status=$(echo "$read_response" | tail -1)
+        read_body=$(echo "$read_response" | sed '$d')
+
+        if [ "$read_status" = "200" ]; then
+            title=$(echo "$read_body" | jq -r '.title // .Title // empty')
+            body=$(echo "$read_body" | jq -r '.body // .Body // empty')
+            status_field=$(echo "$read_body" | jq -r '.status // .Status // empty')
+
+            if [ "$title" = "Verification Test Post" ] && \
+               [ "$body" = "This is an end-to-end verification test." ] && \
+               [ "$status_field" = "Draft" ]; then
+                pass "Create and read content round-trip"
+            else
+                fail "Content fields mismatch -- title='$title', body='$body', status='$status_field'"
+            fi
+        else
+            fail "Read content returned $read_status, expected 200"
+            echo "$read_body"
+        fi
+    else
+        fail "Create response missing content ID"
+        echo "$create_body"
+    fi
+else
+    fail "Create content returned $create_status, expected 201"
+    echo "$create_body"
+fi
+
+# Test 3: Angular app loads
+info "Testing Angular app..."
+web_response=$(curl -s -w '\n%{http_code}' "$WEB_URL")
+web_status=$(echo "$web_response" | tail -1)
+web_body=$(echo "$web_response" | sed '$d')
+
+if [ "$web_status" = "200" ]; then
+    if echo "$web_body" | grep -qi "app-root"; then
+        pass "Angular app loads and contains app-root"
+    else
+        fail "Angular app returned 200 but missing app-root element"
+    fi
+else
+    fail "Angular app returned $web_status, expected 200"
+fi
+
+# Test 4: Persistence across restart
+if [ -n "$CONTENT_ID" ]; then
+    info "Restarting api and db services..."
+    docker compose restart api db
+
+    if wait_for_healthy "$API_URL/health" "API after restart" 60; then
+        persist_response=$(curl -s -w '\n%{http_code}' \
+            "$API_URL/api/content/$CONTENT_ID" \
+            -H "X-Api-Key: $API_KEY")
+
+        persist_status=$(echo "$persist_response" | tail -1)
+        persist_body=$(echo "$persist_response" | sed '$d')
+
+        if [ "$persist_status" = "200" ]; then
+            title=$(echo "$persist_body" | jq -r '.title // .Title // empty')
+            if [ "$title" = "Verification Test Post" ]; then
+                pass "Data persists across container restart"
+            else
+                fail "Data changed after restart -- title='$title'"
+            fi
+        else
+            fail "Read after restart returned $persist_status, expected 200"
+        fi
+    else
+        fail "API did not recover after restart"
+    fi
+else
+    fail "Skipping persistence test -- no content ID from Test 2"
+fi
+
+# Summary
+echo ""
+echo "================================"
+echo "  Verification Summary"
+echo "================================"
+echo -e "  Passed: ${GREEN}${PASS_COUNT}${NC}"
+echo -e "  Failed: ${RED}${FAIL_COUNT}${NC}"
+echo "================================"
+
+if [ "$FAIL_COUNT" -gt 0 ]; then
+    info "Dumping logs for failed services..."
+    docker compose logs --tail=50 api 2>/dev/null || true
+    docker compose logs --tail=50 db 2>/dev/null || true
+    exit 1
+fi
+
+exit 0
