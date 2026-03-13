# Phase 02 — Workflow & Approval Engine: Combined Specification

## Overview

The Workflow & Approval Engine is the central nervous system of the Personal Brand Assistant. Every piece of content flows through it — from AI-generated draft to published post. It manages the content lifecycle state machine, configurable autonomy (the "dial" from fully manual to fully autonomous), approval workflows, scheduled publishing via background jobs, real-time notifications, and an extended audit trail.

## Foundation Context

Phase 01 built the following that Phase 02 extends:

- **Content entity** with state machine (Draft → Review → Approved → Scheduled → Publishing → Published → Failed → Archived) and `TransitionTo()` method with `ContentStateChangedEvent`
- **AutonomyLevel enum** (Manual, Assisted, SemiAuto, Autonomous) and `UserSettings.DefaultAutonomyLevel`
- **AuditLogEntry** entity with automatic interceptor-based logging
- **ContentCalendarSlot** entity with scheduling fields
- **Result<T> pattern**, MediatR CQRS, FluentValidation pipeline
- **EF Core 10 + PostgreSQL 17** with xmin concurrency, JSONB, Testcontainers

## Scope

### 1. Configurable Workflow Engine (IWorkflowEngine)

Replace the hard-coded `Content.AllowedTransitions` dictionary with a configurable, rule-based workflow engine powered by the **Stateless NuGet package**.

**Transition rules are evaluated at three levels** (highest priority wins):
1. **Platform-specific** — e.g., "TwitterX requires review for all content"
2. **Content-type-specific** — e.g., "BlogPost always requires review"
3. **Global** — the base autonomy level from UserSettings

**Autonomy behavior per level:**
- **Manual** — All content must go through Review → Approved before Scheduling
- **Assisted** — Same as Manual (AI creates drafts, human reviews)
- **SemiAuto** — Content with `ParentContentId` (repurposed) auto-approves; new content requires review
- **Autonomous** — All content auto-advances from Draft → Approved → Scheduled (no review step)

**Key rule:** Changing the autonomy level takes effect for **future content only**. Content already in the pipeline keeps its original rules (captured at creation time as a snapshot).

### 2. Autonomy Dial Configuration

**Domain model:** `AutonomyConfiguration` entity storing:
- Global autonomy level (already exists in UserSettings)
- Per-ContentType overrides (Dictionary<ContentType, AutonomyLevel>)
- Per-PlatformType overrides (Dictionary<PlatformType, AutonomyLevel>)
- Per-ContentType-per-Platform overrides (most specific wins)

**Resolution order:** Platform+ContentType > Platform > ContentType > Global

**API endpoints:**
- GET /api/workflow/autonomy — current configuration
- PUT /api/workflow/autonomy — update configuration
- GET /api/workflow/autonomy/resolve?contentType=X&platform=Y — preview resolved level

### 3. Approval Service (IApprovalService)

**Operations:**
- **Approve(contentId)** — transitions Review → Approved (or Approved → Scheduled if ScheduledAt is set)
- **Reject(contentId, feedback)** — transitions Review → Draft with feedback stored in audit
- **EditAndApprove(contentId, changes)** — applies edits then approves
- **BatchApprove(contentIds[])** — approves multiple items atomically

**API endpoints:**
- GET /api/workflow/pending — list content awaiting review (filterable by type, platform)
- POST /api/workflow/approve/{id}
- POST /api/workflow/reject/{id} — body: { feedback: string }
- POST /api/workflow/approve-batch — body: { contentIds: guid[] }

### 4. Content Scheduler (IContentScheduler)

**Operations:**
- **Schedule(contentId, scheduledAt)** — sets ScheduledAt, transitions to Scheduled
- **Reschedule(contentId, newScheduledAt)** — updates timing
- **Cancel(contentId)** — transitions Scheduled → Draft

**Background processing:**
- `ScheduledPublishProcessor` — BackgroundService with PeriodicTimer (checks every 30 seconds)
- Queries for content where `ScheduledAt <= now` and `Status == Scheduled`
- Transitions to Publishing, then calls `IPublishingPipeline` (stub interface for Phase 04)
- On success: transitions to Published, sets PublishedAt
- On failure: increments retry count, applies backoff

**Retry strategy:** 3 retries with exponential backoff (1min, 5min, 15min). After 3 failures, mark as Failed and send notification.

### 5. Background Job Infrastructure

**Architecture:** Channel<T> (bounded, capacity 100) + BackgroundService consumers

**Pattern:**
1. MediatR handler persists work item to database
2. Handler writes to Channel<WorkflowCommand>
3. BackgroundService reads from channel, processes
4. On startup: rehydrate unprocessed items from database into channel

**Job types:**
- **ScheduledPublishProcessor** — processes due scheduled content (PeriodicTimer, 30s interval)
- **RetryFailedProcessor** — retries failed publishes respecting backoff (PeriodicTimer, 60s interval)
- **ContentExpiryProcessor** — archives content past expiry date (PeriodicTimer, 1hr interval)
- **NotificationDispatcher** — reads from notification channel, persists + pushes via SignalR

### 6. Notification System

**Notification entity:**
- Id, UserId, Type (enum: ContentReadyForReview, ContentPublished, ContentFailed, ContentApproved, ContentRejected), Title, Message, ContentId (FK), IsRead, CreatedAt

**INotificationService:**
- Send(notification) — persists to DB + pushes to SignalR
- GetUnread(userId) — list unread notifications
- MarkRead(notificationId)
- MarkAllRead(userId)

**SignalR hub:** `/hubs/notifications`
- Server pushes: WorkflowUpdated, NotificationReceived
- Client receives: real-time toast notifications in Angular

**API endpoints:**
- GET /api/notifications — list notifications (paginated)
- POST /api/notifications/{id}/read
- POST /api/notifications/read-all

### 7. Extended Audit Trail

Build on existing AuditLogInterceptor with workflow-specific extensions:

**WorkflowAuditEntry** (extends or wraps AuditLogEntry):
- FromStatus, ToStatus — content state transition
- Reason — user-provided feedback on rejection, or "auto-approved by autonomy rule"
- ActorType — User, System, Agent (for future AI agent actions)

**API endpoints:**
- GET /api/workflow/audit?contentId=X — audit trail for specific content
- GET /api/workflow/audit?dateFrom=X&dateTo=Y — date range queries

## Out of Scope

- AI content generation (Phase 03, 05)
- Platform API posting implementation (Phase 04) — this engine provides `IPublishingPipeline` stub
- Dashboard UI (Phase 06)
- Email/push notifications (future — in-app only for v1)

## Technology Choices

| Component | Choice | Rationale |
|-----------|--------|-----------|
| State machine | Stateless NuGet v5.20.1 | Lightweight, EF Core compatible, guard clauses |
| Background jobs | BackgroundService + PeriodicTimer | Single-user Docker app, no external deps needed |
| In-process queue | Channel<T> (bounded, 100) | Zero infra, async-native, DB-backed durability |
| Real-time | SignalR | Built-in, zero config for single server |
| Testing | xUnit + Testcontainers + TimeProvider | Follow Phase 01 patterns, control time in tests |

## Interfaces Produced

- `IWorkflowEngine` — submit content, transition state with rules, query status
- `IApprovalService` — approve, reject, batch-approve
- `IContentScheduler` — schedule, cancel, reschedule
- `INotificationService` — send, get unread, mark read
- `IPublishingPipeline` — stub interface for Phase 04 to implement

## Definition of Done

- Content state machine transitions governed by configurable autonomy rules
- Autonomy dial works at global, per-type, and per-platform levels
- Approval queue API functional (list pending, approve, reject, batch-approve)
- Background scheduler processes scheduled content at correct times
- Failed publishes retry 3 times with backoff then notify user
- Notifications delivered via SignalR in real-time
- Audit trail records all transitions with actor and reason
- Integration tests cover state machine edge cases, scheduling, and approval flows
- 80%+ test coverage on new code
