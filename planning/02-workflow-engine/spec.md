# 02 — Workflow & Approval Engine

## Overview
The configurable autonomy dial — the central nervous system of the Personal Brand Assistant. Every piece of content flows through this engine, which manages content lifecycle, approval workflows, scheduling, and background job processing.

## Requirements Reference
See `../requirements.md` for full project context and `../deep_project_interview.md` for design decisions.

Key interview insight: The user wants to "turn the dial both ways" — from fully human-in-the-loop to fully autonomous. This engine must support the entire spectrum.

## Scope

### Content State Machine
States: `Draft → Review → Approved → Scheduled → Publishing → Published → Failed → Archived`

Transitions are governed by:
- Content type (blog posts may always require review; social posts may auto-approve)
- Platform (new platform connections may start in review-required mode)
- Autonomy level (global setting + per-content-type overrides)
- Manual overrides (user can always force a transition)

### Autonomy Dial
A configuration system with levels:
- **Manual** — Everything requires explicit approval before publishing
- **Assisted** — AI creates drafts, user reviews and approves
- **Semi-auto** — Routine content (repurposed posts, scheduled items) auto-publishes; new content needs approval
- **Autonomous** — Everything publishes automatically; user reviews after the fact

Per-content-type and per-platform overrides (e.g., "auto-approve LinkedIn reposts but require approval for new blog posts").

### Approval Workflows
- Content enters review queue when workflow requires it
- User can approve, reject (with feedback), or edit-and-approve
- Batch approval for routine content
- Approval history tracked in audit log

### Scheduling & Queue
- Queue-based async processing for all external actions (posting, publishing)
- Cron-like scheduling (post at specific times, recurring content slots)
- Content calendar integration point (calendar feeds scheduled slots into queue)
- Retry logic for failed publishes (with backoff and max retries)
- Rate limit awareness (don't schedule faster than platform allows)

### Background Jobs
- Hosted services / background worker pattern in .NET
- Job types: ScheduledPublish, RetryFailed, ContentExpiry, EngagementCheck
- Job status tracking and monitoring
- Graceful shutdown handling

### Notifications
- Content ready for review
- Content published successfully
- Content publish failed (with error details)
- Delivery: In-app (dashboard) initially, email/push later

### Audit Trail
- Every state transition logged with: who, when, from-state, to-state, reason
- Queryable audit log API for dashboard

## Out of Scope
- AI content generation (→ 03, 05)
- Platform API posting (→ 04) — this engine triggers it but doesn't implement it
- Dashboard UI for approval (→ 06)

## Key Decisions Needed During /deep-plan
1. State machine implementation — custom vs library (Stateless NuGet)?
2. Background job framework — .NET BackgroundService vs Hangfire vs Quartz.NET?
3. Queue implementation — in-process (Channel<T>) vs external (RabbitMQ/Redis)?
4. Notification delivery mechanism for v1?

## Dependencies
- **Depends on:** `01-foundation` (domain models, database, API structure)
- **Blocks:** `03-agent-orchestration`, `04-platform-integrations`, `06-angular-dashboard`

## Interfaces Consumed
- Domain models from 01 (Content, Platform, User)
- Database context from 01

## Interfaces Produced
- `IWorkflowEngine` — submit content, transition state, query status
- `IApprovalService` — approve, reject, batch-approve
- `IContentScheduler` — schedule, cancel, reschedule
- `INotificationService` — send notifications (consumed by dashboard)
- Workflow API endpoints for dashboard consumption

## Definition of Done
- Content state machine correctly transitions through all states
- Autonomy dial configuration works at global, per-type, and per-platform levels
- Approval queue API functional (submit, approve, reject, list pending)
- Background job scheduler processes scheduled content
- Audit trail records all transitions
- Integration tests cover state machine edge cases
