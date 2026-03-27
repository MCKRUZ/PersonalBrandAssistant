# External Review Integration Notes

## Integrating

1. **Date range semantics** — Define `from` inclusive, `to` inclusive end-of-day. `period` takes precedence unless `from/to` explicitly provided. Previous period = mirror-length window before `from`. Add to plan.

2. **Division by zero** — Return 0 for rate when denominator is 0, return `null` for % change when previous is 0. Add to plan models.

3. **Platform breakdown data gap** — `DailyEngagement` needs `likes/comments/shares` per platform, not just total. Extend the model. Good catch.

4. **Partial failure semantics** — Return composite response with per-section nullable data + errors array + generatedAt. Dashboard can show partial results with staleness per section.

5. **Auth on endpoints** — Analytics endpoints should require API key auth (already in middleware). Add note about `refresh=true` rate limiting.

6. **SSRF on Substack URL** — Allowlist to `*.substack.com` hostnames. Add HttpClient timeout.

7. **DB indexes** — Add explicit index recommendations for `EngagementSnapshots(FetchedAt, ContentPlatformStatusId)` and `ContentPlatformStatuses(ContentId, Platform)`.

8. **HttpClient timeouts/retry** — Add Polly policies for GA4 gRPC and Substack HTTP calls.

9. **Top content endpoint** — Already exists at `GET /api/analytics/top`. Frontend uses it. Add note to extend response with impressions + engagement rate.

10. **GA4 metric validation** — Use `screenPageViews` for web property, validate at startup. Add health check endpoint.

## Not Integrating

1. **DashboardAggregator layer placement** — Keeping it in Infrastructure. It's an orchestration service that depends on external APIs and DB access. The Application layer defines the interface; Infrastructure implements. This follows our existing pattern (e.g., EngagementAggregator is in Infrastructure).

2. **Composite `/api/analytics/page` endpoint** — Not doing this. Parallel frontend calls with `forkJoin` give better UX (progressive loading per section) and simpler error isolation. A single mega-endpoint either succeeds or fails entirely.

3. **DTO versioning** — Overkill for a single-user self-hosted app. Breaking changes are just redeployed together.

4. **Cache tag namespacing for multi-tenant** — Single user, single site. No multi-tenant support needed.

5. **HybridCache L2 with Redis** — Explicitly single-instance. No Redis needed. Plan already states this.

6. **Secrets handling (K8s secrets, etc.)** — This is a Docker Compose self-hosted app, not Kubernetes. Volume mount is appropriate. The `GOOGLE_APPLICATION_CREDENTIALS` env var suggestion is reasonable but our config pattern already works.
