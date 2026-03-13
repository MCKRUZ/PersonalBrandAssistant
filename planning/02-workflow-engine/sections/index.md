<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-domain-entities
section-02-autonomy-configuration
section-03-workflow-engine
section-04-approval-service
section-05-content-scheduler
section-06-notification-system
section-07-background-processors
section-08-api-endpoints
section-09-integration-tests
END_MANIFEST -->

# Phase 02 — Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-domain-entities | - | all | Yes |
| section-02-autonomy-configuration | 01 | 03, 04, 05 | No |
| section-03-workflow-engine | 01, 02 | 04, 05, 07 | No |
| section-04-approval-service | 03 | 08 | Yes |
| section-05-content-scheduler | 03 | 07, 08 | Yes |
| section-06-notification-system | 01 | 07, 08 | Yes |
| section-07-background-processors | 03, 05, 06 | 09 | No |
| section-08-api-endpoints | 04, 05, 06 | 09 | No |
| section-09-integration-tests | all | - | No |

## Execution Order

1. section-01-domain-entities (no dependencies)
2. section-02-autonomy-configuration (after 01)
3. section-03-workflow-engine (after 01, 02)
4. section-04-approval-service, section-05-content-scheduler, section-06-notification-system (parallel after 03)
5. section-07-background-processors (after 03, 05, 06)
6. section-08-api-endpoints (after 04, 05, 06)
7. section-09-integration-tests (final — after all)

## Section Summaries

### section-01-domain-entities
New domain entities (Notification, WorkflowTransitionLog), Content entity modifications (CapturedAutonomyLevel, RetryCount, NextRetryAt, PublishingStartedAt), new enums (NotificationType, ActorType), new domain events, EF Core configurations, and migrations. NuGet package additions (Stateless, SignalR).

### section-02-autonomy-configuration
AutonomyConfiguration entity with Guid.Empty singleton pattern, structured JSONB override arrays, ResolveLevel method with priority hierarchy, EF Core configuration, DataSeeder update, and unit tests for resolution logic.

### section-03-workflow-engine
IWorkflowEngine interface and Stateless-powered implementation. State machine configuration with guards per autonomy level, WorkflowTransitionLog writing, post-commit domain event dispatch, parity test with Content.AllowedTransitions. The single source of truth for all state transitions.

### section-04-approval-service
IApprovalService interface and implementation. Approve, reject, edit-and-approve, batch-approve operations. Delegates to IWorkflowEngine for transitions. Sends notifications on rejection.

### section-05-content-scheduler
IContentScheduler interface and implementation. Schedule, reschedule, cancel operations. Cancel returns to Approved (not Draft). Validates content status and schedule timing.

### section-06-notification-system
INotificationService interface and implementation with persist-first pattern. Channel<T> for notification dispatch. SignalR NotificationHub with API key auth via query string. NotificationDispatcher BackgroundService.

### section-07-background-processors
ScheduledPublishProcessor (atomic claim, 30s polling), RetryFailedProcessor (exponential backoff, 60s polling), WorkflowRehydrator (stuck publishing detection, 5-min threshold), IPublishingPipeline stub (returns failure), retention cleanup service.

### section-08-api-endpoints
All API endpoint groups: WorkflowEndpoints, ApprovalEndpoints, SchedulingEndpoints, NotificationEndpoints. SignalR hub mapping. Program.cs DI registration for all new services.

### section-09-integration-tests
Full workflow integration tests (create → transition → publish → verify audit), background job integration tests with TimeProvider, API endpoint integration tests, SignalR connection tests. Uses Testcontainers.
