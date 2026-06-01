# Section 05 Review: ContentPublisher Refactor

## Critical Issues

**[CRITICAL] EF DbContext concurrent access in parallel secondaries**
`ContentPublisher.cs` lines 206-223: `Task.WhenAll` runs secondary platform tasks in parallel, each calling `db.ContentPlatformPublishes.AnyAsync()` on the same `DbContext`. EF Core `DbContext` is not thread-safe. Concurrent `AnyAsync` calls plus the `Add()` calls in the foreach after will corrupt internal state or throw.

Fix: Run secondary connectors in parallel (`Task.WhenAll` on `PublishToPlatformAsync` calls only), but serialize all DbContext reads/writes after the results return. Alternatively, use `SemaphoreSlim` or create scoped DbContexts per secondary via `IServiceScopeFactory`.

**[CRITICAL] State machine fired after primary failure short-circuit is unreachable, but wrong on idempotent re-entry**
Lines 192-196: When `publishedPrimary` is true (idempotent re-publish), the code still fires the state machine trigger. If content is already `Published`, the `Publish`/`PublishNow` trigger has no self-transition configured, so `Stateless` will throw `InvalidOperationException`. No try/catch wraps this.

Fix: Guard the state machine fire with `if (content.Status != ContentStatus.Published)`, or configure a self-transition `Ignore` in the state machine.

## Suggestions

**State machine trigger selection is correct but fragile.** `Scheduled -> Publish` and `Approved -> PublishNow` map to the state machine config. However, if content enters this method in any other status (checked earlier, but still), `FireAsync` throws unhandled. Wrap in try/catch or check `machine.CanFire(trigger)` first.

**Guid-only overload backward compat is sound.** It delegates to the full overload with `null` targets and `CancellationToken.None`, preserving Hangfire fire-and-forget semantics. The test `PublishAsync_GuidOverload_CallsFullMethodWithNullTargets` confirms this.

**Missing test: idempotent re-publish of primary when secondaries still need publishing.** The `SkipsPlatformWithExistingPublishedRecord` test only covers the primary; no test covers the case where primary is already published but a secondary is not.

## Approved Items

- Handler delegation to `IContentPublisher` is clean; single-responsibility preserved
- Keyed DI resolution via `IServiceProvider.GetKeyedService` is correct
- `DetermineTargetPlatforms` fallback chain (explicit > content > primary-only) is solid
- Immutability: `PublishResult` is a record, `SecondaryOutcomes` exposed as `IReadOnlyList`
- Test coverage is thorough: 14 infra tests + 3 handler tests covering core scenarios
- `TestWebApplicationFactory` correctly swaps `IContentPublisher` mock for `IContentTransformer` mock

**Verdict: BLOCK** -- Two critical issues (DbContext thread safety, idempotent re-publish state machine throw) must be fixed before merge.
