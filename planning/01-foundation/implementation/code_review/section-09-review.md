# Section 09 Code Review

## CRITICAL
1. Hardcoded API key fallback — read from .env instead
2. `set -e` + `((PASS_COUNT++))` crashes on first pass (0++ is falsy)

## HIGH
1. Exit code parity (bash: 0/1, PS: $FailCount)
2. PowerShell checks for curl but never uses it
3. PowerShell doesn't check HTTP 201 on create
4. Missing contentType assertion in CRUD read-back
5. Missing port conflict detection

## MEDIUM
1. Variable scoping in wait_for_healthy
2. No test data cleanup before teardown
3. Linear vs exponential backoff (spec says exponential)

## LOW
1. PowerShell Ctrl+C handling
2. Only dumps api/db logs, not web
