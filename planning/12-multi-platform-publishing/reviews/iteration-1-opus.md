# Opus Review

**Model:** claude-opus-4
**Generated:** 2026-05-26T17:00:00Z

---

## Critical Issues

**1. `Platform` enum is missing `Medium`** — The plan references `Platform.Medium` throughout, but the current enum only has: Blog, Substack, LinkedIn, Twitter, Reddit, YouTube. Add it explicitly to Phase 1 step 1.

**2. `PlatformCredential` inherits from `BaseEntity` which does not exist** — Existing entities don't inherit from a base class. Either define `BaseEntity` or follow existing pattern with own `Guid Id` property.

**3. `Content.TargetPlatforms` uses `List<Platform>` — violates immutability rule** — Coding style mandates `IReadOnlyList<T>` for public surfaces, but EF Core JSON column support has limitations with read-only collection types.

**4. `PublishContent.Command` return type change is breaking** — Existing returns `IRequest<Result>` (non-generic). Plan proposes `IRequest<Result<PublishResult>>`. Need to define `PublishResult` and update all consumers.

**5. Substack connector stores plaintext email/password** — Login flow requires email/password but plan doesn't clarify persistence. Recommendation: do NOT persist Substack password. Require manual re-login when cookies expire.

**6. AES-256 encryption key needs rotation strategy** — If key is lost or rotated, all encrypted tokens become unreadable. Need key version field or explicit re-encrypt migration step.

## Design Concerns

**7. State machine fires BEFORE secondary publishes complete** — Content.Status becomes Published even if all secondary platforms fail. Plan should explicitly state this is intentional.

**8. `IContentPublisher.PublishAsync` signature change breaks Hangfire** — Adding optional parameter may break Hangfire serialization (serializes by reflection). Keep Guid-only overload for Hangfire.

**9. No "partially published" state** — State machine has clean Published state. Failed secondaries sit as Failed ContentPlatformPublish records. Workable but needs prominent UI.

**10. Retry queue polling is wasteful for single-user app** — Use `BackgroundJob.Schedule` for delayed execution instead of polling every minute.

**11. Medium API deprecation risk** — API hasn't been updated since 2017. Include fallback (export markdown for manual paste) and prominent feature flag.

**12. Substack reverse-engineering risk** — Feature flag from day one, log all responses at Debug level, extensive Tiptap test coverage, check for existing markdown-to-Tiptap libraries.

**13. Twitter v2 media + OAuth 2.0 403 fallback is complexity bomb** — Don't build OAuth 1.0a fallback preemptively. Test v2 first, only build fallback if it actually fails.

## Missing Considerations

**14. No CancellationToken on ContentPublisher.PublishAsync** — Add it or document CancellationToken.None for scheduled publishes.

**15. No idempotency protection on publish** — Check for existing Published ContentPlatformPublish record before publishing to prevent duplicates.

**16. OAuth callback URL routing in Docker/Tailscale** — Redirect URIs must match across environments (local dev, Docker, Tailscale Funnel). Needs explicit documentation.

**17. No rate limiting on publish endpoints** — Debounce/idempotency key needed to prevent accidental double-publishes.

**18. Parallel secondary publishing could hit rate limits** — Consider sequential-with-delay instead of Task.WhenAll.

**19. No mention of updating existing tests** — Tests for BlogConnector, ContentStateMachine, and anything mocking IBlogConnector/IContentPublisher must be updated.

**20. No migration rollback strategy** — TargetPlatforms JSON column could lose data on rollback.

## Minor Issues

**21.** PlatformPublishResult uses bool Success instead of Result<T> pattern
**22.** Keep old PublishAsync(Guid) as overload for Hangfire/Reconciler compatibility
**23.** BlogConnector Markdig removal needed to avoid double-conversion after migration
**24.** Keyed scoped registration is correct with IHttpClientFactory
**25.** Frontend section needs Angular specifics (Material, NgRx, OAuth SPA flow)
