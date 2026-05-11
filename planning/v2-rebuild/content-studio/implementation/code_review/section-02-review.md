# Section 02: Content State Machine -- Code Review

## Verdict: WARNING

All 20 tests pass, transitions match the spec, guards and entry actions are correct. Two issues worth addressing before merging -- one HIGH (correctness risk from split `Configure` calls), one MEDIUM (missing guard test case).

---

### HIGH

**[HIGH-1] Double `Configure` calls on `Draft` and `Scheduled` split permits from entry actions**

File: `src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs:17,34,48,63`

`ContentStatus.Draft` is configured at line 17 (permits) and again at line 63 (entry action). `ContentStatus.Scheduled` is configured at line 34 (permits) and again at line 48 (entry action).

Stateless treats multiple `Configure` calls on the same state as additive -- they return the same `StateConfiguration` and merge. So this works today. But it is fragile:

1. A future developer adding a permit to `Draft` might add it at line 17 not realizing there is a second configuration block 46 lines away. Or worse, add a third `Configure` call.
2. It makes the state machine harder to audit -- you cannot read a single block and know all the behavior for a state.
3. If Stateless ever changed this behavior (unlikely but not impossible), it would silently break.

**Fix:** Consolidate each state's full configuration into a single `Configure` call. Chain entry actions with permits.

```csharp
machine.Configure(ContentStatus.Draft)
    .OnEntryAsync(_ =>
    {
        content.ScheduledAt = null;
        content.HangfireJobId = null;
        content.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    })
    .PermitIf(ContentTrigger.SubmitForReview, ContentStatus.Review,
        () => !string.IsNullOrWhiteSpace(content.Body))
    .PermitIf(ContentTrigger.Approve, ContentStatus.Approved,
        () => !string.IsNullOrWhiteSpace(content.Body))
    .Permit(ContentTrigger.Archive, ContentStatus.Archived);

machine.Configure(ContentStatus.Scheduled)
    .OnEntryAsync(_ =>
    {
        content.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    })
    .Permit(ContentTrigger.Publish, ContentStatus.Published)
    .Permit(ContentTrigger.Unschedule, ContentStatus.Approved);
```

This makes each state's behavior self-contained and removes the audit hazard. Same runtime behavior, better maintainability.

---

### MEDIUM

**[MEDIUM-1] Missing test: `Schedule` guard rejects past `ScheduledAt`**

File: `tests/PBA.Application.Tests/Features/ContentStudio/ContentStateMachineTests.cs`

The Schedule guard checks both `content.ScheduledAt.HasValue` AND `content.ScheduledAt > DateTimeOffset.UtcNow`. The test `Fire_Schedule_FromApproved_FailsWhenScheduledAtNull` covers the null case, but there is no test for `ScheduledAt` set to a time in the past. That is a distinct code path through the guard -- the `HasValue` check passes but the comparison fails.

**Fix:** Add a test:

```csharp
[Fact]
public async Task Fire_Schedule_FromApproved_FailsWhenScheduledAtInPast()
{
    var content = CreateContent(ContentStatus.Approved,
        scheduledAt: DateTimeOffset.UtcNow.AddDays(-1));
    var machine = ContentStateMachine.Create(content);

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => machine.FireAsync(ContentTrigger.Schedule));
}
```

**[MEDIUM-2] Test namespace mismatch**

File: `tests/PBA.Application.Tests/Features/ContentStudio/ContentStateMachineTests.cs:5`

The test file lives in `Features/ContentStudio/` but declares namespace `PBA.Application.Tests.Features.Content`. Should be `PBA.Application.Tests.Features.ContentStudio` to match the directory and the source namespace convention. Not a runtime issue (tests still run), but it will confuse IDE navigation and any namespace-based test filtering.

**Fix:** Change line 5 to:

```csharp
namespace PBA.Application.Tests.Features.ContentStudio;
```

**[MEDIUM-3] `DateTimeOffset.UtcNow` in guard makes unit testing non-deterministic**

File: `src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs:31`

The Schedule guard compares `content.ScheduledAt > DateTimeOffset.UtcNow`. This uses the real clock, making the guard behavior time-dependent. The test at line 103 uses `AddDays(1)` which has enough margin to always pass, so this is not broken today. But if a future test needs to verify behavior near the boundary, or if you need to test the Scheduled transition in integration tests with controlled time, the embedded `UtcNow` will fight you.

No fix needed now -- this is a known trade-off documented here for when you add `TimeProvider` to the project. At that point, inject it into `Create()` and use `timeProvider.GetUtcNow()` instead of `DateTimeOffset.UtcNow`.

---

### LOW

**[LOW-1] Entry actions are sync lambdas wrapped in `Task.CompletedTask`**

File: `src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs:39-49,55-59,64-70`

All four `OnEntryAsync` handlers do synchronous work (property assignments) and return `Task.CompletedTask`. Stateless also supports synchronous `OnEntry` actions. Using `OnEntryAsync` isn't wrong -- it works because `FireAsync` is being called -- but it adds unnecessary async ceremony. If you consolidate per HIGH-1 and everything stays sync, `OnEntry` would be slightly cleaner.

Not a functional issue. Keep `OnEntryAsync` if you anticipate future async work in entry actions (logging, etc.).

**[LOW-2] Stateless package added to test project may be unnecessary**

File: `tests/PBA.Application.Tests/PBA.Application.Tests.csproj:17`

The test project already has a `<ProjectReference>` to `PBA.Application`. Since Stateless is a dependency of PBA.Application, its types are transitively available. The explicit `<PackageReference Include="Stateless">` in the test project is redundant unless a test directly references a Stateless type not exposed through the Application layer's public API.

Check whether any test imports `Stateless` directly. If not, the package reference can be removed to keep the dependency graph clean.

---

### Summary

| Priority | Count | Action |
|----------|-------|--------|
| HIGH | 1 | Consolidate split `Configure` calls -- same behavior, eliminates maintenance trap |
| MEDIUM | 3 | Add past-ScheduledAt guard test, fix namespace, note clock dependency |
| LOW | 2 | Optional cleanup |

All transitions, guards, and entry actions match the spec. The machine is correctly isolated from external services. The implementation is sound -- the HIGH is about maintainability, not broken behavior.
