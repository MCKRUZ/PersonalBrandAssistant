# Section 03: Scoped API Keys

## Overview

Upgrades the existing single API key authentication to a scoped system supporting least-privilege access. Two scopes are introduced:

- **`pba-readonly`**: Used by jarvis-monitor and jarvis-hud BFF. Can access read endpoints (GET), SSE streams, and briefing data. Cannot invoke write operations.
- **`pba-write`**: Used by MCP tools via OpenClaw Gateway. Can access all endpoints including create, publish, schedule, and respond operations.

The existing `ApiKeyMiddleware` is refactored to validate keys against a configuration-defined set of scoped keys and enforce scope requirements per endpoint.

## Dependencies

None. This is a foundational section that blocks sections 04-09 (MCP tools need write scope), section 10 (jarvis-monitor needs readonly scope), and section 11 (HUD BFF needs readonly scope).

## Current State

The existing `ApiKeyMiddleware` at `src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs` validates a single API key from the `ApiKey` configuration value. It hashes both the stored key and the provided key with SHA256, then compares them with `CryptographicOperations.FixedTimeEquals`. It exempts `/health` paths and allows key via query parameter for SignalR hub connections.

The current middleware uses the `X-Api-Key` header and falls back to `?apiKey=` query parameter for hub paths.

## Tests (Write First)

Test file: `tests/PersonalBrandAssistant.Application.Tests/Middleware/ScopedApiKeyMiddlewareTests.cs`

Use xUnit with a test `HttpContext` built from `DefaultHttpContext`. Mock `IConfiguration` to provide the key/scope mapping.

```csharp
// Test: readonly key can access GET endpoints
//   Configure readonly key in options
//   Send GET request with readonly key to a read endpoint path
//   Assert middleware calls next (request proceeds)

// Test: readonly key can access SSE endpoint
//   Send GET request with readonly key to /api/events/pipeline
//   Assert middleware calls next

// Test: readonly key cannot access write endpoints (returns 403)
//   Send POST request with readonly key to a write endpoint path
//   Assert response status is 403 Forbidden

// Test: write key can access all endpoints
//   Send GET request with write key to read endpoint -> proceeds
//   Send POST request with write key to write endpoint -> proceeds

// Test: invalid key returns 401
//   Send request with an unrecognized key
//   Assert response status is 401 Unauthorized

// Test: missing key returns 401
//   Send request without X-Api-Key header
//   Assert response status is 401 Unauthorized

// Test: health endpoint is exempt from authentication
//   Send request to /health without any key
//   Assert middleware calls next

// Test: hub paths accept key via query parameter
//   Send request to /hubs/... with key in ?apiKey= query param
//   Assert middleware calls next

// Test: readonly key via query parameter on hub path still respects scope
//   (Hubs are read-oriented, so readonly scope should work)
```

Integration tests in `tests/PersonalBrandAssistant.Application.Tests/Endpoints/`:

```csharp
// Test: GET /api/content/queue-status with readonly key returns 200
// Test: GET /api/content/queue-status with write key returns 200
// Test: POST /api/content-pipeline/create with readonly key returns 403
// Test: POST /api/content-pipeline/create with write key returns 200 (or normal result)
// Test: GET /api/events/pipeline with readonly key starts SSE stream
```

## Configuration

The middleware reads scoped keys from configuration. In `appsettings.json` (values from User Secrets in dev, Azure Key Vault in prod):

```json
{
  "ApiKeys": {
    "ReadonlyKey": "<secret-readonly-key>",
    "WriteKey": "<secret-write-key>"
  }
}
```

Both keys are stored as SHA256 hashes at startup, matching the existing security pattern. The old `ApiKey` configuration value is kept for backward compatibility during migration -- if present and the newer `ApiKeys` section is absent, the middleware falls back to single-key mode where the single key grants write access.

Key storage across systems:
- jarvis-monitor: `PBA_API_KEY` env var set to the readonly key value
- jarvis-hud: `.env.local` with `PBA_API_KEY` set to the readonly key value (accessed only in Next.js Route Handlers, never sent to browser)
- OpenClaw Gateway: MCP server env configuration set to the write key value

## File Paths

### Modified Files

- `src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs` -- Refactored to support scoped keys.
- `src/PersonalBrandAssistant.Api/appsettings.json` -- Add `ApiKeys` section structure (actual secrets via User Secrets).

### New Files

- `src/PersonalBrandAssistant.Api/Middleware/ApiKeyScope.cs` -- Enum and scope resolution logic.
- `tests/PersonalBrandAssistant.Application.Tests/Middleware/ScopedApiKeyMiddlewareTests.cs` -- Unit tests.

## Scope Resolution Design

### ApiKeyScope Enum

```csharp
public enum ApiKeyScope
{
    Readonly,
    Write
}
```

### Endpoint Scope Requirements

The middleware determines the required scope based on the HTTP method and path:

- **Readonly scope is sufficient for:** All `GET` requests (read endpoints, SSE), all `/health` paths (exempt from auth entirely), hub connections.
- **Write scope is required for:** `POST`, `PUT`, `PATCH`, `DELETE` requests.

This method-based approach avoids maintaining a route-by-route allowlist. The convention is simple: reads are readonly, mutations require write. If a future GET endpoint performs a side effect, it can be explicitly tagged, but the current API follows REST conventions cleanly.

### Middleware Flow

1. Check if the path is exempt (`/health`). If yes, pass through.
2. Extract the API key from `X-Api-Key` header or `?apiKey=` query parameter (for hubs).
3. If no key found, return 401.
4. Hash the provided key and compare against both the readonly key hash and the write key hash using `FixedTimeEquals`.
5. If no match, return 401.
6. Determine the matched key's scope (readonly or write).
7. Determine the required scope for this request (based on HTTP method).
8. If the matched scope does not satisfy the required scope, return 403 Forbidden.
9. Store the resolved scope in `HttpContext.Items["ApiKeyScope"]` for downstream use if needed.
10. Call `_next(context)`.

The 403 response uses the same `ProblemDetails` format as the existing 401:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "Forbidden",
  "status": 403,
  "detail": "API key does not have sufficient scope for this operation."
}
```

### Backward Compatibility

If only the legacy `ApiKey` config is present (no `ApiKeys` section), the middleware operates in single-key mode: the single key is treated as a write-scope key (full access). This ensures existing deployments continue working without configuration changes until scoped keys are rolled out.

## Implementation Notes

- Both key hashes are computed once at middleware construction (in the constructor), not per-request. This matches the existing pattern.
- The `FixedTimeEquals` comparison is performed against both hashes on every request to prevent timing attacks from revealing which key matched.
- The resolved scope stored in `HttpContext.Items` can be read by MCP tools or services that need to know the caller's privilege level (e.g., for audit logging the actor).
- Never log the actual key values. Log only the scope that was resolved.
- The existing Angular dashboard will continue using the write key (or can be given its own readonly key if the dashboard is read-only). This is a deployment decision, not a code decision.
