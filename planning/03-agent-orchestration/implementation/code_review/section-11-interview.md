# Section 11 Code Review Interview

## Auto-Fixes Applied

### #1: LogPromptContent default changed to false
Changed default from `true` to `false` in AgentOrchestrationOptions. Set to `false` in appsettings.json (use appsettings.Development.json for `true`).

### #2: Removed ApiKey field from appsettings.json
Removed empty `"ApiKey": ""` field to prevent accidental key commits. API key set via User Secrets or Key Vault.

### #3: IPromptTemplateService factory uses IOptions pattern
Changed from `configuration.GetSection().Get<>()` to `sp.GetRequiredService<IOptions<AgentOrchestrationOptions>>().Value` for consistency.

### #4: DI test scope disposal
Added `IDisposable` to test class, store scope in field, dispose in `Dispose()`.

## Deferred

- String vs enum for DefaultModelTier/Models — matches JSON config binding format, works as-is
- Volatile.Read on MockChatClient.CallCount — test code, no practical impact
- Assembly scanning for capabilities — 5 explicit registrations is fine for now
- SSE error allowlist — current approach is correct, can evolve later
