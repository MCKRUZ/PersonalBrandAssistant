# Integration Notes — Opus Review Feedback

## Integrating

**#1 Add Medium to Platform enum** — Correct, this is a blocking omission. Adding to Phase 1 explicitly.

**#2 Fix BaseEntity reference** — Correct. Will follow existing entity pattern (own `Guid Id` property), not introduce BaseEntity.

**#5 Don't persist Substack password** — Agree. Storing third-party passwords is a security liability. Will change to manual re-login when cookies expire. Store only cookies (encrypted), not credentials.

**#8 Keep Guid-only PublishAsync overload** — Correct, Hangfire serialization is a real concern. Will keep `PublishAsync(Guid contentId)` for Hangfire/Reconciler and add new `PublishAsync(Guid contentId, IReadOnlyList<Platform> targetPlatforms)` overload.

**#10 Use scheduled Hangfire jobs instead of polling** — Better design for single-user app. Replace recurring poll with `BackgroundJob.Schedule` per failed publish.

**#13 Don't build Twitter OAuth 1.0a preemptively** — YAGNI. Remove v1.1 fallback from plan. Test v2 media upload first, document the risk, build fallback only if needed.

**#14 Add CancellationToken to IContentPublisher** — Yes, threading cancellation properly is important.

**#15 Add idempotency check** — Critical safety measure. Check for existing Published record before publishing.

**#19 Update existing tests** — Correct omission. Adding explicit step to Phase 1 for migrating existing BlogConnector and ContentPublisher tests.

**#22 Keep old overload for backward compatibility** — Same as #8, combining.

**#23 Remove Markdig from BlogConnector** — Correct, BlogFormatter handles the conversion now. BlogConnector should use `request.TransformedContent` directly.

## Not Integrating

**#3 TargetPlatforms immutability** — The reviewer correctly notes the tension but also notes existing precedent (`Content.Tags` is `List<string>`). EF Core JSON column support works better with mutable lists. Keeping `List<Platform>` for consistency with existing patterns. This is a known pragmatic compromise.

**#4 PublishContent.Command return type** — The change from `Result` to `Result<PublishResult>` is intentional and the plan already describes the new flow. The breaking change is part of the refactor. Will add explicit note about defining PublishResult contents.

**#6 Encryption key rotation** — Valid concern for production, but over-engineering for current stage (single-user self-hosted app). Adding a note to document key rotation as a future consideration, not implementing key versioning now.

**#7 State machine fires before secondaries** — This is the intended design (primary + best-effort). Adding explicit documentation in the plan that content is "published" once primary succeeds.

**#9 No partially-published state** — Same as #7. Not adding new state machine states. Per-platform status via ContentPlatformPublish records is sufficient.

**#11 Medium fallback** — Feature flags already planned in Phase 6. Not adding manual paste fallback — if Medium dies, we remove the connector.

**#12 Substack feature flag from day one** — Agree in principle, but feature flags are already in Phase 6 step 25. Moving Substack's flag to Phase 3 (when the connector is built).

**#16 OAuth redirect URI documentation** — Operational concern, not architectural. Will add a note to the configuration section but not a separate implementation step.

**#17 Rate limiting on publish endpoints** — Existing endpoints already have rate limiting configured globally. The idempotency check (#15) handles the double-click case better.

**#18 Parallel vs sequential publishing** — Publishing to 3-4 platforms simultaneously is not going to hit rate limits. These are single API calls, not bulk operations. Keeping parallel.

**#20 Migration rollback** — One-way migration is fine for this project. No rollback strategy needed.

**#21 PlatformPublishResult vs Result<T>** — Deliberate deviation. PlatformPublishResult carries platform-specific metadata (URL, post ID, error message) that Result<T> doesn't accommodate well. The connector layer operates below the MediatR pipeline where Result<T> is used.

**#24 & #25** — Minor/informational, no changes needed. Frontend specifics will be handled during frontend implementation planning.
