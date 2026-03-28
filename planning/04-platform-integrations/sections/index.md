<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-domain-entities
section-02-interfaces-models
section-03-ef-core-config
section-04-media-storage
section-05-rate-limiter
section-06-oauth-manager
section-07-content-formatters
section-08-platform-adapters
section-09-publishing-pipeline
section-10-api-endpoints
section-11-background-processors
section-12-di-configuration
END_MANIFEST -->

# Phase 04 Platform Integrations — Implementation Sections

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-domain-entities | - | 02, 03 | Yes |
| section-02-interfaces-models | 01 | 04, 05, 06, 07, 08, 09 | No |
| section-03-ef-core-config | 01 | 05, 06, 09 | Yes (with 02) |
| section-04-media-storage | 02 | 08, 09 | Yes |
| section-05-rate-limiter | 02, 03 | 08, 09 | Yes (with 04, 06, 07) |
| section-06-oauth-manager | 02, 03 | 08, 11 | Yes (with 04, 05, 07) |
| section-07-content-formatters | 02 | 09 | Yes (with 04, 05, 06) |
| section-08-platform-adapters | 02, 04, 05, 06 | 09, 11 | No |
| section-09-publishing-pipeline | 02, 03, 04, 05, 07, 08 | 10, 11 | No |
| section-10-api-endpoints | 06, 09 | 12 | No |
| section-11-background-processors | 06, 08, 09 | 12 | Yes (with 10) |
| section-12-di-configuration | all | - | No |

## Execution Order

1. section-01-domain-entities (no dependencies)
2. section-02-interfaces-models, section-03-ef-core-config (parallel after 01)
3. section-04-media-storage, section-05-rate-limiter, section-06-oauth-manager, section-07-content-formatters (parallel after 02+03)
4. section-08-platform-adapters (after 04, 05, 06)
5. section-09-publishing-pipeline (after 07, 08)
6. section-10-api-endpoints, section-11-background-processors (parallel after 09)
7. section-12-di-configuration (final, after all)

## Section Summaries

### section-01-domain-entities
ContentPlatformStatus entity, OAuthState entity, PlatformPublishStatus enum, Platform entity updates (GrantedScopes).

### section-02-interfaces-models
ISocialPlatform, IOAuthManager, IRateLimiter, IMediaStorage, IPlatformContentFormatter interfaces. All DTOs: PlatformContent, PublishResult, OAuthTokens, ContentValidation, EngagementStats, RateLimitDecision, RateLimitStatus, PlatformIntegrationOptions, MediaStorageOptions.

### section-03-ef-core-config
ContentPlatformStatusConfiguration (composite index, IdempotencyKey unique index, xmin concurrency), OAuthStateConfiguration (State unique index, ExpiresAt index), Platform entity GrantedScopes migration.

### section-04-media-storage
LocalMediaStorage implementation with date-organized file paths, MIME validation, size limits, HMAC-signed URL generation. MediaEndpoints for serving files with signature validation.

### section-05-rate-limiter
DatabaseRateLimiter reading/writing Platform entity RateLimitState JSONB. RateLimitDecision with RetryAt. YouTube quota tracking with PT timezone reset. Instagram publishing limit caching.

### section-06-oauth-manager
OAuthManager with per-platform OAuth URL generation, server-side state validation, PKCE code_verifier storage, code exchange, token refresh, revocation. Scope tracking. Encrypted token storage via IEncryptionService.

### section-07-content-formatters
Twitter (280 chars, thread splitting), LinkedIn (3000 chars), Instagram (2200 chars, media required, carousel), YouTube (title 100 chars, description 5000 chars). Combined FormatAndValidate returning Result<PlatformContent>.

### section-08-platform-adapters
PlatformAdapterBase with token loading, rate limit checking, 401 retry, 429 handling, Polly policies. Twitter, LinkedIn, Instagram, YouTube concrete adapters with typed HttpClients. Platform-specific API interactions.

### section-09-publishing-pipeline
Replace PublishingPipelineStub. Idempotency via IdempotencyKey. Optimistic concurrency lease. Per-platform formatting, rate limit check, media upload, publish, status tracking. PartiallyPublished overall status. INotificationService for partial failures.

### section-10-api-endpoints
PlatformEndpoints (list, auth-url, callback with state validation, disconnect, status with scopes, test-post, engagement). MediaEndpoints (serve with HMAC validation). API key auth.

### section-11-background-processors
TokenRefreshProcessor (5min cycle, threshold-based queries, OAuthState cleanup). PlatformHealthMonitor (15min, scope validation). PublishCompletionPoller (30s, IG container + YT processing status).

### section-12-di-configuration
Wire all services in DependencyInjection.cs. Typed HttpClients + Polly policies. Options binding. Remove PublishingPipelineStub. appsettings.json updates.
