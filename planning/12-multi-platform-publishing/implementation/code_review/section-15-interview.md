# Section 15 Code Review Interview

## Review Summary
- 1 HIGH, 4 MEDIUM, 3 LOW findings
- HIGH-1 auto-fixed
- 2 MEDIUMs auto-fixed (test improvements)
- Remaining items triaged as let-go

## Auto-Fixed (applied without user input)

### HIGH-1: OAuth ClientId/ClientSecret in appsettings.json
- **File:** appsettings.json
- **Fix:** Removed ClientId, ClientSecret, RedirectUri from LinkedIn and Twitter config sections; kept only Enabled flag
- **Rationale:** Prevents developers from accidentally committing secrets in appsettings.json. Options pattern binds fine without placeholder keys.

### MEDIUM-3: Test coverage gap on HttpClient base addresses
- **File:** PublishingDependencyTests.cs
- **Fix:** Added parameterized test `HttpClientFactory_ConfiguresBaseAddress` verifying base addresses for all 4 API connectors (Medium, LinkedIn, Twitter, Substack)
- **Rationale:** Original test only checked client was non-null, not that configuration delegates ran correctly

### MEDIUM-4: Singleton/scoped lifetime test weakness
- **File:** PublishingDependencyTests.cs
- **Fix:** TokenEncryptor singleton test now resolves from two separate scopes (was same scope). Added OAuthService scoped test asserting NotSame across scopes.
- **Rationale:** Same-scope resolution would also pass for scoped registrations

## Let Go (no action needed)

- MEDIUM-1: TokenEncryptor uses IOptions instead of IOptionsMonitor — pre-existing from section-06, not this section's scope
- MEDIUM-2: Substack typed client vs plan's named client — correct for actual connector constructor, documented deviation
- LOW-1: Encryption:Key empty string crashes on startup — correct fail-fast behavior
- LOW-2: AddPublishingDependencies changed to internal — plan anticipated this, needed for test isolation
- LOW-3: Duplicate AddHttpClient calls — idempotent, harmless

## Test Results
19 publishing DI tests passing after fixes. 598 total .NET tests passing.
