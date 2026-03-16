# Code Review: Section 12 -- DI Configuration

## HIGH Severity

| ID | Finding | Fix |
|----|---------|-----|
| DI-01 | Service lifetime mismatch -- Scoped ISocialPlatform delegates resolve transient typed HttpClient adapters. Captive dependency risk. | Use named HttpClients + manual scoped registration, or chain `.Services.AddScoped<T>()` |
| DI-02 | Missing Polly resilience policies on HttpClients (plan required retry + backoff) | Add Microsoft.Extensions.Http.Polly + AddTransientHttpErrorPolicy |
| DI-03 | CustomWebApplicationFactory doesn't remove 3 new background services | Add RemoveService calls for TokenRefreshProcessor, PlatformHealthMonitor, PublishCompletionPoller |

## MEDIUM Severity

| ID | Finding | Fix |
|----|---------|-----|
| DI-04 | Content formatters registered as Scoped but are stateless | Register as Singleton if no scoped deps |
| DI-05 | Missing tests: typed HttpClient verification, background service registration, LinkedIn/Instagram/YouTube options binding | Add tests |
| DI-06 | Instagram Graph API version hardcoded in DI, not configurable | Pull from config like Twitter/LinkedIn |
| DI-07 | DiTestFactory duplicates CustomWebApplicationFactory pattern | Consider reusing shared factory |

## LOW Severity

| ID | Finding | Fix |
|----|---------|-----|
| DI-08 | PlatformIntegrationOptions section name is magic string | Add const SectionName |
| DI-09 | TimeProvider.System + IDateTimeProvider dual registration | Acceptable transitional, add TODO |
| DI-10 | Test Dispose only disposes last scope | Works with xUnit's per-test instances, fragile pattern |
