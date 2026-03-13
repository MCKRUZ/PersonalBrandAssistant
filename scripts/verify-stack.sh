#!/usr/bin/env bash
set -euo pipefail

# Configuration
API_URL="http://localhost:5000"
WEB_URL="http://localhost:4200"
MAX_WAIT=120
PASS_COUNT=0
FAIL_COUNT=0
CONTENT_ID=""

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

pass() {
    echo -e "${GREEN}[PASS]${NC} $1"
    PASS_COUNT=$((PASS_COUNT + 1))
}

fail() {
    echo -e "${RED}[FAIL]${NC} $1"
    FAIL_COUNT=$((FAIL_COUNT + 1))
}

info() {
    echo -e "${YELLOW}[INFO]${NC} $1"
}

cleanup() {
    if [ -n "$CONTENT_ID" ]; then
        curl -s -X DELETE "$API_URL/api/content/$CONTENT_ID" \
            -H "X-Api-Key: $API_KEY" 2>/dev/null || true
    fi
    info "Tearing down stack..."
    docker compose down -v 2>/dev/null || true
}

trap cleanup EXIT

check_prerequisites() {
    local missing=0
    for cmd in docker curl jq; do
        if ! command -v "$cmd" &>/dev/null; then
            echo "ERROR: $cmd is required but not found on PATH"
            missing=1
        fi
    done

    if ! docker compose version &>/dev/null; then
        echo "ERROR: docker compose is required but not available"
        missing=1
    fi

    if [ "$missing" -eq 1 ]; then
        exit 1
    fi
}

check_ports() {
    local conflict=0
    for port in 5000 5432 4200; do
        if ss -tln 2>/dev/null | grep -q ":${port} " || \
           lsof -iTCP:"${port}" -sTCP:LISTEN 2>/dev/null | grep -q LISTEN; then
            echo "ERROR: Port $port is already in use"
            conflict=1
        fi
    done
    if [ "$conflict" -eq 1 ]; then
        exit 1
    fi
}

wait_for_healthy() {
    local url="$1"
    local label="$2"
    local max_wait="${3:-$MAX_WAIT}"
    local elapsed=0
    local delay=2
    local status="000"

    info "Waiting for $label to become healthy ($url)..."

    while [ "$elapsed" -lt "$max_wait" ]; do
        status=$(curl -s -o /dev/null -w '%{http_code}' "$url" 2>/dev/null || echo "000")
        if [ "$status" = "200" ]; then
            info "$label is healthy (${elapsed}s)"
            return 0
        fi
        sleep "$delay"
        elapsed=$((elapsed + delay))
        if [ "$delay" -lt 16 ]; then
            delay=$((delay * 2))
        fi
    done

    echo "ERROR: $label did not become healthy within ${max_wait}s (last status: $status)"
    return 1
}

# Resolve API key from env var or .env file
resolve_api_key() {
    if [ -n "${API_KEY:-}" ]; then
        return
    fi

    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

    if [ -f "$PROJECT_ROOT/.env" ]; then
        API_KEY=$(grep '^API_KEY=' "$PROJECT_ROOT/.env" | cut -d'=' -f2- || true)
    fi

    if [ -z "${API_KEY:-}" ]; then
        echo "ERROR: API_KEY environment variable is required. Set it or ensure .env contains API_KEY."
        exit 1
    fi
}

# Setup
check_prerequisites
resolve_api_key

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$PROJECT_ROOT"

if [ ! -f ".env" ]; then
    info "Creating .env from .env.example..."
    cp .env.example .env
fi

check_ports

# Build and start
info "Building and starting Docker Compose stack..."
docker compose up -d --build

# Test 1: Health check
if wait_for_healthy "$API_URL/health" "API"; then
    status=$(curl -s -o /dev/null -w '%{http_code}' "$API_URL/health")
    if [ "$status" = "200" ]; then
        pass "Stack starts and API health returns 200"
    else
        fail "API health returned $status, expected 200"
    fi
else
    fail "API health endpoint did not respond within ${MAX_WAIT}s"
fi

# Wait for Angular
wait_for_healthy "$WEB_URL" "Angular" 60 || true

# Test 2: CRUD round-trip
info "Testing content CRUD round-trip..."
create_response=$(curl -s -w '\n%{http_code}' \
    -X POST "$API_URL/api/content" \
    -H "Content-Type: application/json" \
    -H "X-Api-Key: $API_KEY" \
    -d '{
        "contentType": "BlogPost",
        "title": "Verification Test Post",
        "body": "This is an end-to-end verification test."
    }')

create_status=$(echo "$create_response" | tail -1)
create_body=$(echo "$create_response" | sed '$d')

if [ "$create_status" = "201" ]; then
    CONTENT_ID=$(echo "$create_body" | jq -r '.id // .Id // empty')

    if [ -n "$CONTENT_ID" ]; then
        read_response=$(curl -s -w '\n%{http_code}' \
            "$API_URL/api/content/$CONTENT_ID" \
            -H "X-Api-Key: $API_KEY")

        read_status=$(echo "$read_response" | tail -1)
        read_body=$(echo "$read_response" | sed '$d')

        if [ "$read_status" = "200" ]; then
            title=$(echo "$read_body" | jq -r '.title // .Title // empty')
            body=$(echo "$read_body" | jq -r '.body // .Body // empty')
            status_field=$(echo "$read_body" | jq -r '.status // .Status // empty')
            content_type=$(echo "$read_body" | jq -r '.contentType // .ContentType // empty')

            if [ "$title" = "Verification Test Post" ] && \
               [ "$body" = "This is an end-to-end verification test." ] && \
               [ "$status_field" = "Draft" ] && \
               [ "$content_type" = "BlogPost" ]; then
                pass "Create and read content round-trip"
            else
                fail "Content fields mismatch -- title='$title', body='$body', status='$status_field', contentType='$content_type'"
            fi
        else
            fail "Read content returned $read_status, expected 200"
            echo "$read_body"
        fi
    else
        fail "Create response missing content ID"
        echo "$create_body"
    fi
else
    fail "Create content returned $create_status, expected 201"
    echo "$create_body"
fi

# Test 3: Angular app loads
info "Testing Angular app..."
web_response=$(curl -s -w '\n%{http_code}' "$WEB_URL")
web_status=$(echo "$web_response" | tail -1)
web_body=$(echo "$web_response" | sed '$d')

if [ "$web_status" = "200" ]; then
    if echo "$web_body" | grep -qi "app-root"; then
        pass "Angular app loads and contains app-root"
    else
        fail "Angular app returned 200 but missing app-root element"
    fi
else
    fail "Angular app returned $web_status, expected 200"
fi

# Test 4: Persistence across restart
if [ -n "$CONTENT_ID" ]; then
    info "Restarting api and db services..."
    docker compose restart api db

    if wait_for_healthy "$API_URL/health" "API after restart" 60; then
        persist_response=$(curl -s -w '\n%{http_code}' \
            "$API_URL/api/content/$CONTENT_ID" \
            -H "X-Api-Key: $API_KEY")

        persist_status=$(echo "$persist_response" | tail -1)
        persist_body=$(echo "$persist_response" | sed '$d')

        if [ "$persist_status" = "200" ]; then
            title=$(echo "$persist_body" | jq -r '.title // .Title // empty')
            if [ "$title" = "Verification Test Post" ]; then
                pass "Data persists across container restart"
            else
                fail "Data changed after restart -- title='$title'"
            fi
        else
            fail "Read after restart returned $persist_status, expected 200"
        fi
    else
        fail "API did not recover after restart"
    fi
else
    fail "Skipping persistence test -- no content ID from Test 2"
fi

# Summary
echo ""
echo "================================"
echo "  Verification Summary"
echo "================================"
echo -e "  Passed: ${GREEN}${PASS_COUNT}${NC}"
echo -e "  Failed: ${RED}${FAIL_COUNT}${NC}"
echo "================================"

if [ "$FAIL_COUNT" -gt 0 ]; then
    info "Dumping logs for failed services..."
    docker compose logs --tail=50 api 2>/dev/null || true
    docker compose logs --tail=50 db 2>/dev/null || true
    docker compose logs --tail=50 web 2>/dev/null || true
    exit 1
fi

exit 0
