# Section 15: DI Registration

## Status: IMPLEMENTED

## Overview

This section consolidates all publishing-related dependency injection registrations into a single `AddPublishingDependencies()` extension method. This method wires up every connector, formatter, transformer, security service, options binding, and HttpClient factory introduced across sections 01-11.

## What Was Built

### Files Created

| File | Purpose |
|------|---------|
| `tests/PBA.Infrastructure.Tests/DependencyInjection/PublishingDependencyTests.cs` | 19 tests: keyed connector resolution (5), keyed formatter resolution (5), transformer, singleton encryptor, scoped OAuth, retry handler, publisher, HttpClient base address verification (4) |

### Files Modified

| File | Change |
|------|--------|
| `src/PBA.Infrastructure/DependencyInjection.cs` | Extracted `AddPublishingDependencies()` (internal static) with options, security, transformation, keyed connectors x5, keyed formatters x5, typed HttpClients x4, retry handler. Moved EncryptionOptions/LinkedInOptions/TwitterOptions/TokenEncryptor/OAuthService registrations from main method into publishing method. Removed duplicate `AddKeyedScoped<IPlatformConnector, BlogConnector>` from main method. |
| `src/PBA.Infrastructure/PBA.Infrastructure.csproj` | Added `Microsoft.Extensions.Http.Resilience` v10.0.0 for LinkedIn client resilience |
| `src/PBA.Api/appsettings.json` | Added `Encryption`, `ContentTransformer`, and `Publishing` configuration sections with non-secret defaults |

## Code Review Fixes Applied

- **HIGH-1**: Removed OAuth ClientId/ClientSecret/RedirectUri from appsettings.json LinkedIn/Twitter sections (prevents secret leakage — options bind fine without placeholder keys)
- **MEDIUM**: Added HttpClient base address assertions for all 4 API connectors (Medium, LinkedIn, Twitter, Substack)
- **MEDIUM**: Strengthened singleton test to use separate scopes; added scoped lifecycle test for OAuthService

## Test Results

19/19 publishing DI tests passing. 598 total .NET tests passing (0 failures).

## Deviations from Plan

1. **`AddPublishingDependencies` is internal, not private**: Plan anticipated this — needed so test project can call it directly without hitting Npgsql datasource builder (test uses InMemory EF Core)
2. **TransformerOptions registration added**: Not in plan, but `ContentTransformer` constructor requires `IOptionsMonitor<TransformerOptions>` — would fail at runtime without it
3. **Substack uses typed HttpClient, not named**: Plan said named client, but `SubstackConnector` constructor takes `HttpClient` directly (typed pattern). Typed client is the correct approach.
4. **ContentTransformer section added to appsettings.json**: Not in plan, needed for TransformerOptions binding
5. **OAuth secrets removed from appsettings.json**: Plan included empty ClientId/ClientSecret fields; code review flagged as security risk. Removed per options pattern best practice.
