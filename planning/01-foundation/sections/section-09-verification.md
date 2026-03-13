# Section 09 -- Verification

## Overview

This section covers end-to-end Docker Compose validation, confirming that the entire foundation stack works together as an integrated system. The verification is performed after all other sections (01-08) are complete. It validates that the API, database, and Angular frontend all start correctly, communicate with each other, and persist data across container restarts.

**Dependencies:** This section depends on section-06 (Docker), section-07 (Angular), and section-08 (Testing). All must be fully implemented before verification can run.

## Background

The Personal Brand Assistant foundation consists of three Docker Compose services:

- **api** -- .NET 10 Minimal API on port 5000 (host) mapped from 8080 (container), depends on db
- **db** -- PostgreSQL 17 Alpine on port 5432, with named volume `pgdata` for persistence
- **web** -- Nginx serving Angular static build on port 4200 (host) mapped from 80 (container), depends on api

The API uses API key authentication via the `X-Api-Key` header. The key is configured through the `API_KEY` environment variable. The liveness endpoint `GET /health` is exempt from auth; all other endpoints require the key.

## Tests First

These verification tests are not xUnit tests. They are scripted integration checks run against a live Docker Compose stack. Implement them as a shell script (`scripts/verify-stack.sh`) and optionally as a PowerShell script (`scripts/verify-stack.ps1`) for Windows compatibility.

### Test Definitions

**Test 1: Full Docker Compose stack starts, API returns health 200**

- Run `docker compose up -d --build` from the project root
- Wait for all three services to report healthy (use `docker compose ps` or poll health endpoints)
- Send `GET http://localhost:5000/health` (no API key required)
- Assert HTTP 200 response

**Test 2: Create content via API, read it back, verify fields match**

- Send `POST http://localhost:5000/api/content` with header `X-Api-Key: <configured-key>` and JSON body:
  ```json
  {
    "contentType": "BlogPost",
    "title": "Verification Test Post",
    "body": "This is an end-to-end verification test."
  }
  ```
- Assert HTTP 201 response with a content ID in the response body
- Send `GET http://localhost:5000/api/content/{id}` with the returned ID and same API key header
- Assert HTTP 200 response
- Assert returned `title` equals "Verification Test Post"
- Assert returned `body` equals "This is an end-to-end verification test."
- Assert returned `status` equals "Draft"
- Assert returned `contentType` equals "BlogPost"

**Test 3: Angular app loads and displays dashboard**

- Send `GET http://localhost:4200`
- Assert HTTP 200 response
- Assert response body contains `<app-root>` or the Angular app title text (confirming the SPA shell loaded)

**Test 4: Database persists data across container restart**

- After Test 2 has created content, run `docker compose restart api db`
- Wait for services to become healthy again (poll `GET http://localhost:5000/health` until 200)
- Send `GET http://localhost:5000/api/content/{id}` with the same ID from Test 2
- Assert HTTP 200 response
- Assert the content fields still match the original values

### Cleanup

After all tests pass (or fail), tear down the stack:

- Run `docker compose down -v` to remove containers and volumes (use `-v` only for test runs; omit in production)

## Implementation Details

### File: `scripts/verify-stack.sh`

A bash script that orchestrates the verification. Structure:

```bash
#!/usr/bin/env bash
set -euo pipefail

# Configuration
API_URL="http://localhost:5000"
WEB_URL="http://localhost:4200"
API_KEY="${API_KEY:-<default-dev-key>}"  # Match .env.example default
MAX_WAIT=120  # seconds to wait for services
```

Key implementation points:

- Use `curl` for HTTP requests with `-s` (silent) and `-w '%{http_code}'` for status code extraction
- Implement a `wait_for_healthy` function that polls the health endpoint with exponential backoff up to `MAX_WAIT` seconds
- Use `jq` for JSON parsing of API responses (document as a prerequisite)
- Print clear pass/fail output for each test with descriptive messages
- Exit with code 0 on full success, non-zero on any failure
- Always run cleanup (`docker compose down`) in a trap handler so the stack is torn down even on script failure

### File: `scripts/verify-stack.ps1` (Optional)

PowerShell equivalent for Windows environments. Use `Invoke-RestMethod` and `Invoke-WebRequest` instead of curl. Same test logic.

### File: `.env.example` (Reference)

The verification script sources or references the `.env.example` file for default values. This file is created in section-06 with these variables:

- `DB_PASSWORD` -- PostgreSQL password
- `API_KEY` -- API authentication key
- `ASPNETCORE_ENVIRONMENT` -- Development/Production
- `DPKEYS_PATH` -- Data Protection key storage path

For verification, copy `.env.example` to `.env` if it does not exist, populating with test-safe defaults.

### Prerequisites

The verification script should check for required tools before running:

- `docker` and `docker compose` available on PATH
- `curl` available (bash script)
- `jq` available for JSON parsing (bash script)
- No port conflicts on 5000, 5432, 4200

### Script Flow

1. Check prerequisites
2. Copy `.env.example` to `.env` if `.env` does not exist
3. Run `docker compose up -d --build`
4. Wait for API health endpoint to return 200 (up to 120 seconds)
5. Wait for Angular app to return 200 (up to 60 seconds)
6. Execute Test 1 (health check)
7. Execute Test 2 (CRUD round-trip)
8. Execute Test 3 (Angular serves)
9. Execute Test 4 (persistence across restart)
10. Print summary (passed/failed counts)
11. Tear down with `docker compose down`
12. Exit with appropriate code

### Error Reporting

Each test should output:

```
[PASS] Stack starts and API health returns 200
[PASS] Create and read content round-trip
[FAIL] Angular app loads -- expected 200, got 000 (connection refused)
[PASS] Data persists across restart
```

On failure, capture and display relevant diagnostic info:

- `docker compose logs --tail=50 <service>` for the failing service
- The actual HTTP response body if status code was unexpected

## Verification Checklist

After running the script successfully, confirm:

- All four tests pass
- No error logs in `docker compose logs api`
- No error logs in `docker compose logs db`
- Angular app renders without console errors (manual browser check)
- The entire startup-to-verification cycle completes in under 5 minutes on a clean build
