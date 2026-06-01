# Section 01: Interfaces and Types -- Code Review

**Reviewer:** code-reviewer agent
**Verdict:** APPROVE with suggestions

No critical or high-severity issues. The implementation matches the plan exactly, enum integer stability is preserved, and all types are well-designed for downstream consumption. Three suggestions worth considering before moving on.

---

## Findings

### [SUGGESTION] Two records in one file violates one-class-per-file convention
**File:** `src/PBA.Application/Common/Models/PublishResult.cs:10-15`
**Severity:** Suggestion

`PublishResult` and `PlatformPublishOutcome` are defined in the same file. The project convention (from coding-style rules and the section plan file summary table) is one type per file. `PlatformPublishOutcome` should live in its own file.

The plan itself lists `PublishResult.cs` as containing both records, so this is a plan-level decision -- but it contradicts the project standard. Worth splitting now since downstream sections (05, 07-10) will import `PlatformPublishOutcome` independently.

**Fix:**
```
src/PBA.Application/Common/Models/PublishResult.cs         -> PublishResult only
src/PBA.Application/Common/Models/PlatformPublishOutcome.cs -> PlatformPublishOutcome only
```

---

### [SUGGESTION] PlatformPublishRequest missing ScheduledAt for PublishMode.Schedule
**File:** `src/PBA.Application/Common/Models/PlatformPublishRequest.cs:6-12`
**Severity:** Suggestion

`PlatformPublishRequest` has `PublishMode Mode` which includes `Schedule`, but no `DateTimeOffset? ScheduledAt` property to tell the connector *when* to schedule. Section-05 constructs requests with `PublishMode.Publish` and the `Content` entity carries `ScheduledAt`, so connectors can read `request.Content.ScheduledAt` -- but that couples connectors to the full `Content` entity for a single field.

Sections 07-10 (Medium, LinkedIn, Twitter, Substack) all reference `PublishMode.Schedule` and some explicitly note the platform does not support it. Those that do support scheduling would need the timestamp from somewhere.

Two options:
1. Add `DateTimeOffset? ScheduledAt` to `PlatformPublishRequest` now (clean, self-contained request).
2. Leave as-is -- connectors read `request.Content.ScheduledAt` (works, slightly less clean).

If you go with option 2, that is fine -- `Content` is already on the request. But option 1 is a better contract since it makes the request self-documenting. Flag for a decision before section-05 implementation.

---

### [SUGGESTION] PublishMode enum could benefit from explicit integer values
**File:** `src/PBA.Domain/Enums/PublishMode.cs:3-7`
**Severity:** Suggestion

`Platform` was correctly given explicit integer values to protect EF Core persistence. `PublishMode` uses implicit values. If `PublishMode` is ever persisted as an integer (e.g., on a `ContentPlatformPublish` record or in a Hangfire job argument), adding a value between existing members would cause the same shifting problem Platform had.

Currently `PublishMode` is not stored in the database, so this is low-risk. But applying the same explicit-value discipline defensively costs nothing:

```csharp
public enum PublishMode
{
    Draft = 0,
    Publish = 1,
    Schedule = 2
}
```

---

### [NOTE] Platform enum ordering differs from plan
**File:** `src/PBA.Domain/Enums/Platform.cs:3-10`
**Severity:** Note (no action needed)

The plan initial suggestion placed `Medium` after `Blog` (second position) for logical grouping. The implementation correctly chose the safer approach from the plan own risk analysis: explicit integer values with `Medium = 6` appended at the end. This is the right call given EF Core stores Platform as `int`. Existing data is preserved. No action needed -- just noting the deliberate deviation from the first code block in the plan.

---

### [NOTE] IContentTransformer returns string, not PreprocessedContent
**File:** `src/PBA.Application/Common/Interfaces/IContentTransformer.cs:7`
**Severity:** Note (by design)

`IContentTransformer.TransformAsync` returns `Task<string>` while `IPlatformFormatter.FormatAsync` takes `PreprocessedContent`. This means `IContentTransformer` internally preprocesses and then delegates to the appropriate `IPlatformFormatter`, returning the final formatted string to the caller. The intermediate `PreprocessedContent` is internal to the transformation pipeline.

This is the correct design -- callers (ContentPublisher in section-05) get a simple string, and the preprocessing + formatting complexity is encapsulated. Documenting the data flow for downstream implementers:

```
Content + Platform -> IContentTransformer.TransformAsync
  -> preprocess to PreprocessedContent (internal)
  -> resolve IPlatformFormatter by Platform
  -> IPlatformFormatter.FormatAsync(PreprocessedContent) -> string
  -> return string
```

---

### [NOTE] Record types use IReadOnlyList correctly
**Files:** All model records
**Severity:** Note (positive)

`PlatformCapabilities.SupportedMediaTypes`, `PreprocessedContent.Tags`, `PreprocessedContent.Images`, `PlatformPublishRequest.Tags`, and `PublishResult.SecondaryOutcomes` all use `IReadOnlyList<T>`. This matches the immutability conventions.

Note: `Content.Tags` is `List<string>` on the entity, so section-05 will need to pass it directly (List implements IReadOnlyList) or wrap it. No issue, just something to be aware of.

---

## Correctness vs. Plan

| Plan Item | Implementation | Match |
|-----------|---------------|-------|
| Platform enum: add Medium with explicit int values | Medium = 6, all values explicit | Yes |
| PublishMode enum: Draft, Publish, Schedule | Exact match | Yes |
| IPlatformConnector: Platform, PublishAsync, ValidateCredentialsAsync, GetCapabilities | Exact match | Yes |
| IPlatformFormatter: Platform, FormatAsync | Exact match | Yes |
| IContentTransformer: TransformAsync(Content, Platform, ct) | Exact match | Yes |
| PlatformPublishRequest: Content, TransformedContent, Tags, CanonicalUrl, Mode | Exact match | Yes |
| PlatformPublishResult: Success, PublishedUrl, PlatformPostId, ErrorMessage | Exact match | Yes |
| PlatformCapabilities: all 7 properties | Exact match | Yes |
| PreprocessedContent: Title, Body, CanonicalUrl, Tags, Images | Exact match | Yes |
| ImageReference: OriginalPath, AbsoluteUrl, AltText | Exact match | Yes |
| PublishResult + PlatformPublishOutcome | Exact match (two records in one file) | Yes |

All 11 files match the plan. No missing members. No extra members.

---

## Downstream Risk Assessment

| Downstream Section | Risk | Notes |
|-------------------|------|-------|
| Section 03 (Transformation) | None | IContentTransformer and IPlatformFormatter contracts are clean |
| Section 04 (Blog Migration) | None | IPlatformConnector contract matches IBlogConnector responsibilities |
| Section 05 (Publisher) | Low | ScheduledAt gap (see suggestion above); PublishResult shape is correct |
| Sections 07-10 (Connectors) | None | PlatformPublishRequest and PlatformPublishResult are complete |

---

## Summary

Clean section. The types are correct, immutable, and well-shaped for downstream use. The explicit enum integer values are the right safety measure. Three optional improvements identified -- the two-records-in-one-file split is the most actionable since it is a direct convention violation, though minor. No blockers for proceeding to section-02 or section-03.
