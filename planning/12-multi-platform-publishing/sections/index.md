<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-interfaces-and-types
section-02-domain-model-changes
section-03-content-transformation
section-04-blog-connector-migration
section-05-publisher-refactor
section-06-encryption-and-oauth
section-07-medium-connector
section-08-linkedin-connector
section-09-twitter-connector
section-10-substack-connector
section-11-retry-handler
section-12-api-endpoints
section-13-frontend-connections
section-14-frontend-editor
section-15-di-registration
END_MANIFEST -->

# Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-interfaces-and-types | - | 03, 04, 05, 07-10 | Yes |
| section-02-domain-model-changes | - | 05, 06, 07-12 | Yes |
| section-03-content-transformation | 01 | 04, 07-10 | No |
| section-04-blog-connector-migration | 01, 03 | 05 | No |
| section-05-publisher-refactor | 01, 02, 04 | 07-12 | No |
| section-06-encryption-and-oauth | 02 | 07-10, 12, 13 | No |
| section-07-medium-connector | 01, 03, 05, 06 | 15 | Yes |
| section-08-linkedin-connector | 01, 03, 05, 06 | 15 | Yes |
| section-09-twitter-connector | 01, 03, 05, 06 | 15 | Yes |
| section-10-substack-connector | 01, 03, 05, 06 | 15 | Yes |
| section-11-retry-handler | 02, 05 | 15 | Yes |
| section-12-api-endpoints | 05, 06 | 14 | No |
| section-13-frontend-connections | 06, 12 | - | Yes |
| section-14-frontend-editor | 12 | - | Yes |
| section-15-di-registration | 07, 08, 09, 10, 11 | - | No |

## Execution Order

1. **Batch 1:** section-01-interfaces-and-types, section-02-domain-model-changes (parallel, no dependencies)
2. **Batch 2:** section-03-content-transformation (after 01)
3. **Batch 3:** section-04-blog-connector-migration (after 01, 03)
4. **Batch 4:** section-05-publisher-refactor (after 01, 02, 04)
5. **Batch 5:** section-06-encryption-and-oauth (after 02)
6. **Batch 6:** section-07-medium-connector, section-08-linkedin-connector, section-09-twitter-connector, section-10-substack-connector, section-11-retry-handler (parallel, all after 01, 03, 05, 06)
7. **Batch 7:** section-12-api-endpoints (after 05, 06)
8. **Batch 8:** section-13-frontend-connections, section-14-frontend-editor (parallel, after 06/12)
9. **Batch 9:** section-15-di-registration (after all connectors and retry handler)

## Section Summaries

### section-01-interfaces-and-types
Add `Medium` to Platform enum. Define `IPlatformConnector`, `IPlatformFormatter`, `IContentTransformer` interfaces and supporting record types (`PlatformPublishRequest`, `PlatformPublishResult`, `PlatformCapabilities`, `PublishResult`, `PlatformPublishOutcome`, `PreprocessedContent`, `ImageReference`, `PublishMode`). All in the Application layer.

### section-02-domain-model-changes
Create `PlatformCredential` entity. Add `TargetPlatforms` JSON column to `Content`. Add `RetryCount` and `NextRetryAt` to `ContentPlatformPublish`. Create EF migration. Configure value converters.

### section-03-content-transformation
Implement `ContentTransformer` with shared preprocessing (frontmatter stripping, image path resolution). Implement `BlogFormatter` (markdown→HTML via Markdig, template application). Register via keyed DI. Tests for preprocessor and BlogFormatter.

### section-04-blog-connector-migration
Adapt `BlogConnector` to implement `IPlatformConnector` instead of `IBlogConnector`. Remove internal Markdig conversion — use `request.TransformedContent`. Remove `IBlogConnector` interface. Update existing BlogConnector tests.

### section-05-publisher-refactor
Refactor `ContentPublisher` to use keyed DI resolution. Implement multi-platform publish flow (primary + best-effort). Add idempotency checks. Keep Guid-only overload for Hangfire. Update `PublishContent` command to accept `TargetPlatforms`. Update existing ContentPublisher tests.

### section-06-encryption-and-oauth
Implement `ITokenEncryptor` (AES-256-GCM). Implement `IOAuthService` with LinkedIn and Twitter support (authorization URL, code exchange, PKCE, token refresh, state validation). Create options classes for each platform.

### section-07-medium-connector
Implement `MediumFormatter` (canonical URL injection, SVG→PNG references, absolute image URLs). Implement `MediumConnector` (REST API v1, bearer token auth, /me endpoint, post creation). HttpClient factory registration. Tests with mocked HTTP responses.

### section-08-linkedin-connector
Implement `LinkedInFormatter` (markdown→plain text, 3000-char truncation, "Read more" link). Implement `LinkedInConnector` (OAuth token validation/refresh, image upload two-step, post creation with versioned headers). Tests with mocked HTTP responses.

### section-09-twitter-connector
Implement `TwitterFormatter` (markdown→plain text, thread splitting at sentence boundaries, 280-char segments). Implement `TwitterConnector` (PKCE token refresh, chunked media upload, tweet/thread creation via reply chain). Tests with mocked HTTP responses.

### section-10-substack-connector
Implement `SubstackFormatter` (markdown→Tiptap JSON conversion, subscribe widget injection, references/bio stripping). Implement `SubstackConnector` (cookie-based auth, draft creation, image CDN upload, publishing). Feature flag from day one. Debug logging for all API payloads. Tests with mocked HTTP responses and captured Tiptap examples.

### section-11-retry-handler
Implement `IPublishRetryHandler` with `BackgroundJob.Schedule` pattern. Exponential backoff (5 min, 30 min, 2 hours). Max 3 retries. Idempotency check. Permanent failure marking.

### section-12-api-endpoints
Add OAuth endpoints (authorize, callback, status, disconnect). Add platform management endpoints (list platforms, store credentials). Update content publish/schedule endpoints to accept `targetPlatforms`. Add retry and publish-status endpoints.

### section-13-frontend-connections
Platform connections settings page. OAuth connect/disconnect for LinkedIn/Twitter. Token entry for Medium. Login form for Substack. Connection status indicators.

### section-14-frontend-editor
Platform target checkboxes on content editor. Character count per platform. Publish confirmation modal with per-platform preview and toggle. Content list status badges.

### section-15-di-registration
Wire all connectors, formatters, services, and options in `AddPublishingDependencies()`. Keyed DI for IPlatformConnector and IPlatformFormatter. HttpClient factories. Hangfire job registration.
