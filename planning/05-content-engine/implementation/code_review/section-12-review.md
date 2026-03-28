# Code Review: Section 12 -- Docker Compose & DI Configuration

**Reviewer:** code-reviewer agent
**Date:** 2026-03-16
**Diff:** section-12-diff.md
**Spec:** sections/section-12-docker-di-config.md

---

## Summary

This section adds Docker Compose definitions for sidecar, TrendRadar, and FreshRSS services, wires all content engine services into the DI container, binds three new options classes, registers four background processors, and adds DI + Docker validation tests. The implementation is largely solid but deviates from the spec in several important ways.

---

## Issues Found

### CRITICAL-01: Missing ISidecarClient singleton registration

**File:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

**Description:** The spec explicitly requires adding ISidecarClient as a singleton to replace the removed IChatClientFactory. The diff does not include this registration. The tests only work because they inject a MockSidecarClient via ConfigureTestServices, masking the fact that production will fail to resolve ISidecarClient.

**Impact:** Any service depending on ISidecarClient (ContentPipeline, BrandVoiceService, agents) will throw at runtime in production.

**Fix:** Add the singleton registration in DependencyInjection.cs:

```csharp
services.AddSingleton<ISidecarClient, SidecarClient>();
```

---

### CRITICAL-02: IChatClientFactory removal not executed

**File:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

**Description:** The spec states to remove the IChatClientFactory singleton registration. The diff shows no removal of this registration. Both the old IChatClientFactory and new sidecar services coexist, which contradicts the spec and leaves dead code in the container.

**Impact:** Confusing dual registration, potential for code to resolve the deprecated factory instead of the sidecar client.

**Fix:** Remove the IChatClientFactory registration line. Also add the two missing spec tests: ISidecarClient_Resolves_AsSingleton and IChatClientFactory_NoLongerRegistered.

---

### CRITICAL-03: Missing SidecarHealthCheck registration

**File:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

**Description:** The spec (section 5) requires adding a SidecarHealthCheck to the health checks pipeline. The diff shows no health check addition. Without this, there is no way to detect sidecar connectivity issues through the health endpoint.

**Impact:** Production monitoring blind spot -- the API could report healthy while the sidecar is unreachable.

**Fix:** Add to the existing AddHealthChecks() chain:

```csharp
services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>()
    .AddCheck<SidecarHealthCheck>("sidecar");
```

---

### HIGH-01: FreshRSS port 8080 exposed in base docker-compose.yml

**File:** `docker-compose.yml` (line 79-80 of diff)

**Description:** The freshrss service publishes 8080:80 in the base docker-compose.yml. Publishing ports in the base compose file means production deployments expose the FreshRSS admin UI to the host network by default.

**Impact:** In production, FreshRSS admin interface is accessible on port 8080 without authentication gating from the application. This is an unnecessary attack surface.

**Fix:** Move the `ports: - "8080:80"` mapping from docker-compose.yml to docker-compose.override.yml (dev only). In production, FreshRSS should only be accessible via the internal Docker network from the API service.

---

### HIGH-02: ContentEngineOptions has extra fields not in spec

**File:** `src/PersonalBrandAssistant.Api/appsettings.json` (lines 126-129 of diff)

**Description:** The ContentEngine config section includes MaxTreeDepth and SlotMaterializationDays which are not defined in the spec ContentEngineOptions class. The spec defines only four fields: BrandVoiceScoreThreshold, MaxAutoRegenerateAttempts, EngagementRetentionDays, EngagementAggregationIntervalHours.

**Impact:** If the options POCO does not have matching properties, these values silently fail to bind and are ignored. If the POCO does have them, the class deviates from spec without documentation.

**Fix:** Either (a) add MaxTreeDepth and SlotMaterializationDays to the ContentEngineOptions class and update the spec, or (b) remove them from appsettings.json if they belong elsewhere.

### HIGH-03: TrendMonitoringOptions missing RelevanceScoreThreshold and MaxSuggestionsPerCycle

**File:** `src/PersonalBrandAssistant.Api/appsettings.json` (lines 135-136 of diff)

**Description:** The appsettings includes RelevanceScoreThreshold (0.6) and MaxSuggestionsPerCycle (10) under TrendMonitoring, but the spec TrendMonitoringOptions class does not define these properties. Same binding issue as HIGH-02.

**Impact:** These configuration values will be silently ignored unless the POCO class has matching properties.

**Fix:** Either add these properties to TrendMonitoringOptions and update the spec, or remove them from appsettings if they are consumed elsewhere.

---

### HIGH-04: Missing tests for ISidecarClient singleton resolution

**File:** `tests/.../DependencyInjection/ContentEngineServiceRegistrationTests.cs`

**Description:** The spec lists 12 test methods. The diff implements only 10. Missing tests:

1. ISidecarClient_Resolves_AsSingleton -- verifies singleton registration and same-instance behavior
2. IChatClientFactory_NoLongerRegistered -- verifies the old factory no longer resolves

These are the two tests that validate the most critical migration change (factory-to-sidecar swap).

**Fix:** Add both tests as specified.

---

### HIGH-05: Existing test factories not updated for ISidecarClient

**File:** `tests/.../DependencyInjection/ContentEngineServiceRegistrationTests.cs`

**Description:** The spec section 6 (IChatClientFactory Removal Checklist) states that AgentServiceRegistrationTests.cs and PlatformServiceRegistrationTests.cs must be updated to replace MockChatClientFactory with MockSidecarClient. The diff does not show these modifications.

**Impact:** If the IChatClientFactory registration is removed (as it should be per CRITICAL-02), existing test suites will fail because they still inject MockChatClientFactory.

**Fix:** Update both existing test factory classes to inject MockSidecarClient for ISidecarClient instead of MockChatClientFactory for IChatClientFactory.

---

### LOW-01: Sidecar build context references sibling repo without documentation

**File:** `docker-compose.yml` (line 33 of diff)

**Description:** `build: context: ../claude-code-sidecar` assumes a specific sibling directory layout. There is no comment or documentation in the compose file explaining this dependency or how to set it up.

**Fix:** Add a comment in docker-compose.yml and document this in the project README or a setup guide.

---

### LOW-02: Sidecar prompts volume mount is read-only but no output volume

**File:** `docker-compose.yml` (line 39 of diff)

**Description:** The sidecar mounts `./prompts:/config/prompts:ro` but the spec mentions mounting "blog output directory and prompts directory." Only prompts are mounted. If the sidecar needs to write blog output, it has no writable volume for that purpose.

**Fix:** Verify whether the sidecar needs a writable output volume. If so, add one.

---

### LOW-03: TrendRadar missing environment variables for alert configuration

**File:** `docker-compose.yml` (lines 68-74 of diff)

**Description:** The spec states TrendRadar should have "Environment variables for alert configuration (Telegram bot token via env vars, not hardcoded)." The diff shows no environment variables for the trendradar service.

**Impact:** TrendRadar alert functionality will not work without configuration.

**Fix:** Add placeholder environment variables referencing .env file.

---

### LOW-04: PostgreSQL restart policy bundled into this diff

**File:** `docker-compose.yml` (line 29 of diff)

**Description:** The diff adds `restart: unless-stopped` to the db service. This is good but unrelated to the content engine scope. Minor concern -- noting it as a bonus fix.

---

### LOW-05: Docker compose test uses fragile text parsing

**File:** `tests/.../Docker/DockerComposeValidationTests.cs` (lines 411-432 of diff)

**Description:** The SidecarService_NotPublishedToExternalPorts test parses YAML as plain text with regex. The regex could match YAML keys inside the sidecar block that happen to have 2-space indentation. This is fragile.

**Impact:** Low -- the test works for the current file structure but could produce false positives/negatives if compose file formatting changes.

**Fix:** Consider using a YAML parsing library (YamlDotNet) for more robust validation, or at minimum add a comment acknowledging the fragility.

---

## Spec Compliance Summary

| Spec Requirement | Status |
|-----------------|--------|
| ISidecarClient singleton registration | MISSING |
| IChatClientFactory removal | MISSING |
| SidecarHealthCheck registration | MISSING |
| SidecarOptions binding | Present |
| ContentEngineOptions binding | Present (extra fields in appsettings) |
| TrendMonitoringOptions binding | Present (extra fields in appsettings) |
| 6 scoped content engine services | Present |
| 4 background hosted services | Present |
| Sidecar Docker service (no ports) | Present, correct |
| TrendRadar Docker service | Present, missing env vars |
| FreshRSS Docker service | Present, port exposure concern |
| Internal network | Present, correct |
| 12 DI tests | 10 of 12 implemented |
| Docker validation test | Present |
| Existing test factory updates | MISSING |

---

## Verdict: BLOCK

Three CRITICAL issues and five HIGH issues must be resolved before merge. The most important gaps are the missing ISidecarClient registration (production will fail) and the missing IChatClientFactory removal (the spec primary migration goal is not achieved). The FreshRSS port exposure in the base compose file is a security concern that should be addressed.

**Required before approval:**
1. Add ISidecarClient singleton registration
2. Remove IChatClientFactory registration
3. Add SidecarHealthCheck to health checks
4. Move FreshRSS port mapping to override file
5. Add the two missing test methods
6. Update existing test factories
7. Reconcile appsettings fields with options POCOs
