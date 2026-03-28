# Phase 02 — Workflow Engine Interview

## Q1: Autonomy dial change behavior
**Question:** For the autonomy dial, should changing the level take effect immediately (mid-pipeline content keeps its original rules) or retroactively (pending content re-evaluates against the new level)?

**Answer:** Immediate only — new level applies to future content only. Content already in pipeline keeps its original rules.

## Q2: Publish failure retry strategy
**Question:** When a scheduled publish fails (e.g., platform API is down), how many retries before surfacing to the user?

**Answer:** 3 retries with exponential backoff — retry at 1min, 5min, 15min — then mark Failed and notify user.

## Technical Decisions (Made from Research)

The following decisions were made based on comprehensive codebase exploration and technology research, without needing user input:

- **State machine:** Stateless NuGet — lightweight, EF Core compatible, guard clauses for autonomy rules
- **Background jobs:** BackgroundService + Channel<T> — single-user self-hosted app doesn't need Hangfire/Quartz
- **Queue:** In-process Channel<T> (bounded, ~100 capacity) with database-backed durability
- **Notifications:** SignalR for real-time push, PostgreSQL for persistence/offline resilience
- **Testing:** xUnit + Testcontainers + TimeProvider for time control
