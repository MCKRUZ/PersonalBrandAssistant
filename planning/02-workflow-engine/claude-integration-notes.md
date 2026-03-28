# Phase 02 — External Review Integration Notes

## Review Source: OpenAI GPT-5.2

22 findings total. Here's what I'm integrating and what I'm not.

---

## Integrating (High Impact)

### 1. Two state machines = divergence risk (#1)
**Integrating.** Make `WorkflowEngine` the single source of truth that calls `Content.TransitionTo()` internally. Add a parity test asserting both agree on allowed transitions.

### 2. Cancel schedule: Scheduled → Approved, not Draft (#3)
**Integrating.** Canceling a schedule should revert to Approved (content was already approved, just unscheduled). Makes more sense than Draft.

### 3. Publishing stub should NOT mark Published (#4)
**Integrating.** Stub returns `Result<Unit>.Failure(ErrorCode.InternalError, "Publishing pipeline not implemented")`. Content stays in Publishing/Failed state until Phase 04 is integrated. This prevents false Published state.

### 4. Atomic claim for scheduled publishing (#6)
**Integrating.** Use `UPDATE contents SET status='Publishing' WHERE id=@id AND status='Scheduled' AND scheduled_at<=now RETURNING *` to atomically claim work. Prevents double-publish even in edge cases.

### 5. SignalR auth — don't exempt /hubs/ (#8)
**Integrating.** Require API key in SignalR negotiation via query string parameter (`?apiKey=xxx`). Validate in `OnConnectedAsync`. Don't exempt the path.

### 6. Notification service: persist first, push best-effort (#12)
**Integrating.** DB write first, then SignalR push in try/catch. Never push if persistence failed.

### 7. Singleton AutonomyConfiguration: fixed deterministic key (#19)
**Integrating.** Use a fixed primary key (`Guid.Empty` or a well-known constant). Always query by that key.

### 8. Dictionary JSON key brittleness (#20)
**Integrating.** Switch from `"ContentType:PlatformType"` string keys to a structured JSON array of `{ contentType, platformType, level }` objects.

### 9. Stuck Publishing detection (#15)
**Integrating.** Add `PublishingStartedAt` timestamp. Rehydrator treats content in Publishing for more than 5 minutes as stuck and requeues.

### 10. Polling indexes (#14)
**Integrating.** Add composite indexes: `(Status, ScheduledAt)` for scheduled queries, `(Status, NextRetryAt)` for retry queries. Use `AsNoTracking()` for read operations.

### 11. Post-commit event dispatch (#11)
**Integrating.** Domain events dispatched after `SaveChangesAsync` succeeds. Notification handlers respond to events, not called inline during transition.

### 12. Retention for WorkflowTransitionLog and Notifications (#13)
**Integrating.** Extend `AuditLogCleanupService` (or add companion) to also clean WorkflowTransitionLog and Notifications older than configurable thresholds.

---

## Partially Integrating

### 13. SemiAuto via ParentContentId (#2)
**Partially integrating.** Keep `ParentContentId` as the mechanism but add a clearer domain method: `Content.IsDerivedContent` (returns true if ParentContentId is set AND parent content exists in Published/Approved status). This is good enough for v1 — a dedicated `SourceContentId` field is over-engineering at this stage.

### 14. Channel design inconsistency (#5)
**Partially integrating.** Simplify: remove separate channels. Use polling-only design for processors (simpler, fewer moving parts). Channel<T> only for notifications (event-driven, immediate push). This eliminates the complexity the reviewer flagged while keeping the notification path fast.

### 15. Assisted autonomy level behavior (#16)
**Integrating definition.** Assisted = AI creates drafts in Draft status, auto-transitions to Review, notifies user. Same as Manual for approval (user must approve). The difference is that Manual doesn't auto-create — user manually triggers AI generation.

---

## Not Integrating

### 16. Replace ParentContentId with SourceContentId (#2 detailed)
**Not integrating.** `ParentContentId` is already in the schema, and its semantics (content derived from other content) are clear enough. Renaming adds migration complexity for no real benefit in v1.

### 17. Content expiry contradictions (#18)
**Not integrating.** Content expiry is a low-priority feature. Removing ContentExpiryProcessor from v1 scope — it can be added in a later phase when we have clear requirements. The plan was already vague here.

### 18. Multi-user security hardening (#9, #10)
**Not integrating deeply.** This is a single-user app. The UserId field exists for future extensibility but we're not building multi-tenant security now. Workflow manual transition endpoint stays — it's behind API key auth which is sufficient for single-user self-hosted.

### 19. DST/timezone edge cases (#7)
**Not integrating as major change.** `ScheduledAt` is already `DateTimeOffset` (UTC). The ContentCalendarSlot stores timezone info for display purposes. The processor queries by UTC. DST handling for recurring patterns will be addressed in Phase 05 (content calendar).

### 20. Concurrency/double-publish integration tests (#21)
**Deferring.** The atomic claim query handles this at the SQL level. Writing concurrent integration tests for a single-user app is over-testing for v1. Will add if we ever scale.

### 21. SignalR auth tests (#22)
**Deferring.** Will test SignalR connection manually. Auth on the hub is sufficient for single-user Docker setup.

### 22. Rewrite processor architecture (#reviewer suggestion)
**Not integrating.** The reviewer offered to rewrite the processor design. The simplified polling + atomic claim + stuck detection is robust enough. No need for the full event-driven + reconciliation pattern in v1.
