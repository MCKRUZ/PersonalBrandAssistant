# Section 09 Code Review Interview

## Triage Decision
All items triaged autonomously per user preference.

## Auto-Fixed (CRITICAL + HIGH)
1. **Hardcoded API key fallback** — Removed. Both scripts now resolve API_KEY from env var or .env file, fail if missing.
2. **`set -e` + `((PASS_COUNT++))` crash** — Changed to `PASS_COUNT=$((PASS_COUNT + 1))` syntax.
3. **Exit code parity** — Both scripts now exit 0 on success, 1 on failure.
4. **PowerShell false curl prerequisite** — Removed curl from PS prerequisites.
5. **PowerShell missing HTTP 201 status check** — Uses `Invoke-WebRequest` for create call, checks StatusCode.
6. **Missing contentType assertion** — Added to both scripts.
7. **Port conflict detection** — Added `check_ports` (bash) and `Test-PortAvailable` (PS).
8. **Variable scoping in wait_for_healthy** — Moved `local status` before loop.
9. **Exponential backoff** — Changed to true exponential (`delay *= 2`, cap at 16).
10. **Test data cleanup** — Added DELETE call in cleanup trap.
11. **Web service log dump** — Added `docker compose logs --tail=50 web`.

## Let Go
1. PowerShell Ctrl+C handling — try/finally is sufficient for this use case.
2. Build timeout — Docker's own timeouts handle this; documenting expected time isn't needed for a dev script.
