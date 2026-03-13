# Phase 02 — Workflow & Approval Engine: TDD Plan

## Testing Stack

From Phase 01 research:
- **Framework:** xUnit
- **Mocking:** Moq
- **Integration:** Testcontainers.PostgreSql
- **Time control:** TimeProvider (built-in .NET 8+)
- **API testing:** CustomWebApplicationFactory with test API key
- **Test data:** TestEntityFactory with static factory methods
- **Conventions:** Tests in `tests/PersonalBrandAssistant.Domain.Tests/`, `tests/PersonalBrandAssistant.Application.Tests/`, `tests/PersonalBrandAssistant.Infrastructure.Tests/`, `tests/PersonalBrandAssistant.Api.Tests/`

---

## 4. Domain Layer Changes

### AutonomyConfiguration Entity
- Test: ResolveLevel returns GlobalLevel when no overrides exist
- Test: ResolveLevel returns ContentType override when it matches
- Test: ResolveLevel returns Platform override when it matches (wins over ContentType)
- Test: ResolveLevel returns ContentType+Platform override when both match (wins over all)
- Test: ResolveLevel falls through correctly when specific override is missing
- Test: Constructor sets Id to Guid.Empty (singleton enforcement)
- Test: Override value objects serialize/deserialize correctly

### Notification Entity
- Test: Constructor sets IsRead to false by default
- Test: MarkAsRead sets IsRead to true

### WorkflowTransitionLog Entity
- Test: Constructor sets Timestamp
- Test: All required fields populated on creation

### Content Entity Modifications
- Test: CapturedAutonomyLevel is set at creation and immutable
- Test: PublishingStartedAt is set when transitioning to Publishing
- Test: RetryCount defaults to 0
- Test: NextRetryAt is nullable and unset by default

### Domain Events
- Test: ContentApprovedEvent contains correct ContentId
- Test: ContentRejectedEvent contains ContentId and Feedback
- Test: ContentScheduledEvent contains ContentId and ScheduledAt
- Test: ContentPublishedEvent contains ContentId and Platforms

---

## 5. Application Layer

### IWorkflowEngine (via WorkflowEngine)
- Test: TransitionAsync succeeds for valid transition (Draft → Review)
- Test: TransitionAsync fails for invalid transition (Draft → Published)
- Test: TransitionAsync creates WorkflowTransitionLog entry
- Test: Auto-approval triggers for Autonomous level (Draft → Review → Approved automatically)
- Test: Auto-approval triggers for SemiAuto when ParentContentId is set and parent is Published
- Test: SemiAuto does NOT auto-approve when ParentContentId is null
- Test: Manual requires explicit approval (stays in Review)
- Test: Assisted requires explicit approval (same as Manual for approval step)
- Test: CapturedAutonomyLevel governs transitions, not current global level
- Test: Domain events dispatched after SaveChanges (post-commit)
- Test: xmin concurrency conflict returns error
- Test: State machine parity — Stateless config and Content.AllowedTransitions agree on all valid transitions

### IApprovalService
- Test: ApproveAsync transitions Review → Approved
- Test: ApproveAsync chains to Scheduled when ScheduledAt is set
- Test: ApproveAsync fails when content is not in Review status
- Test: RejectAsync transitions Review → Draft with feedback in audit log
- Test: RejectAsync sends ContentRejected notification
- Test: EditAndApproveAsync applies changes then transitions to Approved
- Test: BatchApproveAsync approves multiple items, returns success count
- Test: BatchApproveAsync handles partial failures (some items not in Review)

### IContentScheduler
- Test: ScheduleAsync sets ScheduledAt and transitions Approved → Scheduled
- Test: ScheduleAsync fails when content is not Approved
- Test: ScheduleAsync fails when scheduledAt is in the past
- Test: RescheduleAsync updates ScheduledAt on Scheduled content
- Test: CancelAsync transitions Scheduled → Approved (not Draft)
- Test: CancelAsync clears ScheduledAt

### INotificationService
- Test: SendAsync persists notification to database
- Test: SendAsync pushes to SignalR after DB write succeeds
- Test: SendAsync does NOT push to SignalR if DB write fails
- Test: SendAsync handles SignalR push failure gracefully (notification still persisted)
- Test: GetUnreadAsync returns only unread notifications, newest first
- Test: MarkReadAsync sets IsRead to true
- Test: MarkAllReadAsync marks all user's notifications as read

### IPublishingPipeline (Stub)
- Test: PublishAsync returns Failure with ErrorCode.InternalError and message "Publishing pipeline not implemented"

### MediatR Commands/Queries
- Test: TransitionContentCommand validates ContentId is not empty
- Test: ConfigureAutonomyCommand updates AutonomyConfiguration correctly
- Test: ResolveAutonomyLevelQuery returns resolved level for given type/platform
- Test: ApproveContentCommand calls IApprovalService.ApproveAsync
- Test: RejectContentCommand validates Feedback is not empty
- Test: BatchApproveContentCommand validates ContentIds is not empty
- Test: ListPendingContentQuery returns only Review-status content with pagination
- Test: ScheduleContentCommand validates ScheduledAt is in the future
- Test: ListNotificationsQuery returns paginated results with optional IsRead filter

---

## 6. Infrastructure Layer

### WorkflowEngine (Stateless Integration)
- Test: Stateless state machine configures all expected transitions
- Test: Guard clauses correctly evaluate autonomy levels
- Test: External state storage correctly reads/writes Content.Status

### Channel<T> and WorkflowChannelService
- Test: NotificationCommand channel is bounded (capacity 100)
- Test: Write and read from notification channel works

### ScheduledPublishProcessor
- Test: Picks up content where Status=Scheduled AND ScheduledAt<=now (use TimeProvider)
- Test: Uses atomic claim query (content transitions atomically to Publishing)
- Test: Sets PublishingStartedAt when claiming content
- Test: Calls IPublishingPipeline for claimed content
- Test: On failure, increments RetryCount and sets NextRetryAt
- Test: Does not pick up content where ScheduledAt is in the future

### RetryFailedProcessor
- Test: Picks up content where Status=Failed AND RetryCount<3 AND NextRetryAt<=now
- Test: Respects backoff timing (1min, 5min, 15min)
- Test: After 3 failures, sends notification and leaves in Failed status
- Test: Does not pick up content where NextRetryAt is in the future
- Test: Does not pick up content where RetryCount >= 3

### WorkflowRehydrator
- Test: On startup, detects content stuck in Publishing for >5 minutes
- Test: Resets stuck content back to Scheduled for reprocessing
- Test: Does not touch content in Publishing for <5 minutes

### SignalR NotificationHub
- Test: Hub connection requires API key in query string
- Test: Hub rejects connection without valid API key
- Test: Server can push NotificationReceived event
- Test: Server can push WorkflowUpdated event

### EF Core Configurations
- Test: AutonomyConfiguration enforces singleton (Guid.Empty PK)
- Test: AutonomyConfiguration JSONB columns store/retrieve overrides correctly
- Test: Notification indexes exist (UserId, IsRead, CreatedAt DESC)
- Test: WorkflowTransitionLog indexes exist (ContentId, Timestamp DESC)
- Test: Content composite indexes exist (Status, ScheduledAt) and (Status, NextRetryAt)

### Retention Cleanup
- Test: Cleans WorkflowTransitionLog entries older than threshold
- Test: Cleans Notification entries older than threshold
- Test: Does not clean entries within threshold
- Test: Thresholds are configurable

---

## 7. API Layer

### WorkflowEndpoints
- Test: PUT /api/workflow/autonomy updates configuration and returns 200
- Test: GET /api/workflow/autonomy returns current configuration
- Test: GET /api/workflow/autonomy/resolve returns resolved level
- Test: POST /api/workflow/{id}/transition performs transition and returns 200
- Test: POST /api/workflow/{id}/transition returns 400 for invalid transition
- Test: GET /api/workflow/{id}/transitions returns allowed transitions
- Test: GET /api/workflow/audit returns filtered audit entries

### ApprovalEndpoints
- Test: GET /api/approval/pending returns Review-status content
- Test: POST /api/approval/{id}/approve transitions and returns 200
- Test: POST /api/approval/{id}/reject with feedback transitions to Draft
- Test: POST /api/approval/batch-approve approves multiple and returns count

### SchedulingEndpoints
- Test: POST /api/scheduling/{id}/schedule sets schedule and returns 200
- Test: PUT /api/scheduling/{id}/reschedule updates timing
- Test: DELETE /api/scheduling/{id} cancels schedule (returns to Approved)

### NotificationEndpoints
- Test: GET /api/notifications returns paginated list
- Test: GET /api/notifications?isRead=false returns only unread
- Test: POST /api/notifications/{id}/read marks as read
- Test: POST /api/notifications/read-all marks all as read

### Auth
- Test: All endpoints require API key
- Test: SignalR negotiation requires API key in query string

---

## 9. Migration and Seed Data

- Test: DataSeeder creates default AutonomyConfiguration (GlobalLevel=Manual, no overrides)
- Test: New Content columns have correct defaults (CapturedAutonomyLevel=0, RetryCount=0, NextRetryAt=null, PublishingStartedAt=null)

---

## Integration Test Scenarios

### Full Workflow Flow
- Test: Create content → transition Draft → Review → Approved → Scheduled → Publishing → verify audit trail has all transitions
- Test: Create content with Autonomous level → verify auto-advances through Draft → Approved → Scheduled
- Test: Create content → approve → schedule → cancel → verify returns to Approved

### Notification Flow
- Test: Reject content → verify notification created with correct type and message
- Test: Failed publish after 3 retries → verify failure notification sent

### Background Job Flow (TimeProvider)
- Test: Schedule content for future time → advance TimeProvider → verify processor picks it up
- Test: Fail publish → advance TimeProvider past backoff → verify retry processor picks it up
- Test: Content stuck in Publishing for 6 minutes → verify rehydrator resets it
