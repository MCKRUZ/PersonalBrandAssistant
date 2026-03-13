# Code Review: Section 01 - Domain Entities (Agent Orchestration)

**Reviewer:** code-reviewer agent
**Date:** 2026-03-13
**Scope:** AgentExecution, AgentExecutionLog entities; AgentCapabilityType, AgentExecutionStatus, ModelTier enums; unit tests
**Verdict:** APPROVE with minor suggestions

---

## Summary

Clean implementation of two domain entities with well-defined state machine logic, proper encapsulation, and thorough test coverage. The code follows established project conventions consistently. No critical or high-severity issues found.

---

## Critical Issues

None.

---

## Warnings (should fix)

### [W-01] RecordUsage has no input validation or state guard

**File:** AgentExecution.cs (lines 89-103 in diff)

RecordUsage accepts any values without validation and can be called in any state, including after completion or failure. Negative token counts and negative costs would be silently accepted.

Contrast with every other mutation method on the entity, which validates preconditions before mutating state.

**Suggested fix -- add guard clauses at the top of RecordUsage:**

    ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
    ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
    ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
    ArgumentOutOfRangeException.ThrowIfNegative(cacheReadTokens);
    ArgumentOutOfRangeException.ThrowIfNegative(cacheCreationTokens);
    ArgumentOutOfRangeException.ThrowIfNegative(cost);

Whether to add a state guard (e.g., reject recording usage on a Cancelled execution) is a design decision -- it may be valid to record usage after failure if the LLM call did consume tokens before erroring. Document the intended behavior either way.

### [W-02] Fail method accepts arbitrary-length error string with no length guard

**File:** AgentExecution.cs (line 66 in diff)

AgentExecutionLog.Content is truncated to 2000 chars, but AgentExecution.Error has no such protection. A stack trace or verbose error message could be unbounded. If this persists to a database nvarchar(max) column, it could store unexpectedly large payloads.

**Suggested fix:** Apply a similar truncation or max-length constant, or ensure the EF configuration enforces a max length.

### [W-03] AgentExecutionLog.Create has no validation on stepNumber or stepType

**File:** AgentExecutionLog.cs (lines 128-142 in diff)

stepNumber could be zero or negative, and stepType could be null or empty. The entity would silently accept invalid data.

**Suggested fix:**

    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepNumber);
    ArgumentException.ThrowIfNullOrWhiteSpace(stepType);

---

## Suggestions (consider improving)

### [S-01] Consider using a domain exception instead of InvalidOperationException

**File:** AgentExecution.cs (all state transition methods)

The existing Content.TransitionTo also uses InvalidOperationException, so this is consistent today. However, as the domain grows, a custom InvalidStateTransitionException (with From, To, EntityType properties) would make it easier for application-layer code to distinguish domain rule violations from infrastructure errors. This is a future consideration, not a blocker.

### [S-02] Test gap: no test for RecordUsage on a completed/failed/cancelled execution

**File:** AgentExecutionTests.cs

There is a test for RecordUsage on a Running execution, but no test establishing whether it should succeed or fail on terminal states. Adding these tests would document the intended behavior regardless of whether a state guard is added.

### [S-03] Test gap: no negative-value tests for RecordUsage

**File:** AgentExecutionTests.cs

If input validation is added per W-01, corresponding tests for negative token counts, negative cost, and null/empty modelId should be added.

### [S-04] Consider making StepType a strongly-typed enum or string constant class

**File:** AgentExecutionLog.cs (line 123 in diff)

StepType is a free-form string ("prompt", "completion", etc.). This is flexible but risks typos and inconsistency. A StepType enum or a static class with string constants would provide compile-time safety. This depends on whether step types are expected to be extensible at runtime.

### [S-05] Minor: Duration calculation reuses CompletedAt correctly

**File:** AgentExecution.cs (lines 61-63, 73-75, 85-86 in diff)

The pattern of setting CompletedAt first, then computing Duration from it, is correct. It avoids a second UtcNow call. No action needed; noting it as a positive pattern.

### [S-06] Consider adding a navigation property from AgentExecution to its logs

**File:** AgentExecution.cs

The entity has no IReadOnlyList of AgentExecutionLog as a Logs collection. This is fine if the relationship is configured only at the infrastructure layer, but adding a navigation property would enable domain-level invariants like "an execution must have at least one log entry to complete." This is a design choice, not a defect.

---

## Pattern Consistency Check

| Pattern | Expected | Actual | Status |
|---------|----------|--------|--------|
| Private parameterless constructor | private ctor for EF Core | Present on both entities | PASS |
| Static Create() factory method | Single entry point | Present on both entities | PASS |
| private init on creation props | Immutable after creation | All creation props use private init | PASS |
| private set on mutable props | Controlled mutation | Status, Error, tokens, cost, etc. | PASS |
| UUIDv7 via Guid.CreateVersion7() | Inherited from EntityBase | Confirmed in base class | PASS |
| Base class inheritance | AuditableEntityBase for auditable | AgentExecution: AuditableEntityBase, AgentExecutionLog: EntityBase | PASS |
| State transitions throw InvalidOperationException | Matches Content.TransitionTo | All guard clauses consistent | PASS |
| Enums as single-line files | Matches existing pattern | All three enums follow convention | PASS |
| Test AAA pattern | Arrange-Act-Assert | Consistently followed | PASS |
| Test helpers (CreatePending, CreateRunning) | Matches ContentTests.CreateDraft | Consistent | PASS |

---

## Security Check

| Check | Result |
|-------|--------|
| Hardcoded credentials | None |
| SQL injection risk | N/A (domain layer, no DB access) |
| Input validation at boundary | Partial -- see W-01, W-02, W-03 |
| Sensitive data in Error field | Low risk -- error messages should not contain user PII, but worth noting for future application-layer code that calls Fail() |

---

## Test Coverage Assessment

| Area | Covered | Notes |
|------|---------|-------|
| AgentExecution.Create (all params) | Yes | 7 tests |
| MarkRunning (valid + invalid) | Yes | 2 tests |
| Complete (valid + invalid + output) | Yes | 4 tests |
| Fail (from Running, Pending, invalid) | Yes | 4 tests |
| Cancel (from Running, Pending, invalid) | Yes | 4 tests |
| RecordUsage | Partial | Only happy path, no edge cases (S-02, S-03) |
| State transition matrix (Theory) | Yes | 10 transitions via parameterized test |
| AgentExecutionLog.Create | Yes | 7 tests including truncation edge cases |
| AgentExecutionLog null content | Yes | 1 test |

Estimated coverage: ~90% of branches. The gaps are in RecordUsage edge cases.

---

## Verdict

**APPROVE** -- No critical or high issues. The code is clean, well-structured, and follows all established project conventions. The warnings (W-01 through W-03) around input validation are worth addressing before merge but are not blocking -- the domain layer is not a public API boundary, and the application layer will add its own FluentValidation. The suggestions are quality-of-life improvements for future maintainability.
