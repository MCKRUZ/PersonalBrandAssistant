# Section 03: DTOs and Validators

## Overview

This section creates all request/response DTOs and their FluentValidation validators for the Content Studio feature. These DTOs form the contract between API endpoints (section-11) and command/query handlers (sections 04, 05, 06). Validators are auto-discovered via assembly scanning and applied through the existing MediatR validation pipeline behavior.

## Dependencies

- **section-01-schema-updates** must be complete: the `Content` entity needs `HangfireJobId`, `IsDeleted`, and `Children` properties. The `IAppDbContext` must expose `ContentPlatformPublishes` and `BrandProfiles` DbSets.
- Existing domain enums: `ContentStatus`, `ContentType`, `Platform`, `PublishStatus` (all in `PBA.Domain.Enums`)
- Existing domain entities: `Content`, `ContentPlatformPublish`, `BrandProfile` (all in `PBA.Domain.Entities`)

## Blocks This

- **section-04-query-handlers** (uses response DTOs for projections)
- **section-05-core-commands** (uses request DTOs as command payloads)
- **section-11-api-endpoints** (binds request DTOs from HTTP, returns response DTOs)

---

## Tests First

All validator tests go in:
`tests/PBA.Application.Tests/Features/Content/Validators/`

The project already has validator tests that follow a consistent pattern using `FluentValidation.TestHelper`. Each test file instantiates the validator, constructs valid/invalid inputs, and asserts with `ShouldHaveValidationErrorFor` / `ShouldNotHaveAnyValidationErrors`.

### Test File: CreateContentRequestValidatorTests.cs

**Path:** `tests/PBA.Application.Tests/Features/Content/Validators/CreateContentRequestValidatorTests.cs`

| Test Name | Setup | Assertion |
|-----------|-------|-----------|
| `Validate_EmptyTitle_HasError` | Title = "" | Error on Title |
| `Validate_TitleExceeds200Chars_HasError` | Title = new string('x', 201) | Error on Title |
| `Validate_InvalidContentType_HasError` | ContentType = (ContentType)999 | Error on ContentType |
| `Validate_InvalidPlatform_HasError` | PrimaryPlatform = (Platform)999 | Error on PrimaryPlatform |
| `Validate_ValidRequest_NoErrors` | Valid title, valid enums | No errors |

### Test File: UpdateContentRequestValidatorTests.cs

**Path:** `tests/PBA.Application.Tests/Features/Content/Validators/UpdateContentRequestValidatorTests.cs`

| Test Name | Setup | Assertion |
|-----------|-------|-----------|
| `Validate_BodyExceeds100KChars_HasError` | Body = new string('x', 100_001) | Error on Body |
| `Validate_MissingLastUpdatedAt_HasError` | LastUpdatedAt = default | Error on LastUpdatedAt |
| `Validate_ValidRequest_NoErrors` | Valid body under limit, LastUpdatedAt set | No errors |

### Test File: DraftContentRequestValidatorTests.cs

**Path:** `tests/PBA.Application.Tests/Features/Content/Validators/DraftContentRequestValidatorTests.cs`

| Test Name | Setup | Assertion |
|-----------|-------|-----------|
| `Validate_InvalidAction_HasError` | Action = "invalid_action" | Error on Action |
| `Validate_InstructionsExceeds2000Chars_HasError` | Instructions = new string('x', 2001) | Error on Instructions |
| `Validate_ValidDraftAction_NoErrors` | Action = "draft" | No errors |
| `Validate_ValidRefineAction_NoErrors` | Action = "refine" | No errors |
| `Validate_ValidShortenAction_NoErrors` | Action = "shorten" | No errors |
| `Validate_ValidExpandAction_NoErrors` | Action = "expand" | No errors |
| `Validate_ValidChangeToneAction_NoErrors` | Action = "changeTone" | No errors |

### Test File: ScheduleContentRequestValidatorTests.cs

**Path:** `tests/PBA.Application.Tests/Features/Content/Validators/ScheduleContentRequestValidatorTests.cs`

| Test Name | Setup | Assertion |
|-----------|-------|-----------|
| `Validate_PastDate_HasError` | ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5) | Error on ScheduledAt |
| `Validate_FutureDate_NoErrors` | ScheduledAt = DateTimeOffset.UtcNow.AddHours(1) | No errors |

### Test File: CrossPostRequestValidatorTests.cs

**Path:** `tests/PBA.Application.Tests/Features/Content/Validators/CrossPostRequestValidatorTests.cs`

Note: The "same platform as content's primary" guard cannot be validated purely in the validator because it requires loading the Content entity to compare. This validation belongs in the command handler.

| Test Name | Setup | Assertion |
|-----------|-------|-----------|
| `Validate_InvalidPlatform_HasError` | TargetPlatform = (Platform)999 | Error on TargetPlatform |
| `Validate_ValidPlatform_NoErrors` | TargetPlatform = Platform.LinkedIn | No errors |

---

## Implementation

### Response DTOs

All response DTOs go in: `src/PBA.Application/Features/Content/Dtos/`

Each DTO is a `record` with `init`-only properties. Use `IReadOnlyList<T>` for collections on public surfaces.

#### ContentDto.cs

**Path:** `src/PBA.Application/Features/Content/Dtos/ContentDto.cs`

List-view DTO. Projected from `Content` entity in query handlers.

Properties:
- `Guid Id`
- `string Title`
- `ContentType ContentType`
- `ContentStatus Status`
- `Platform PrimaryPlatform`
- `decimal? VoiceScore`
- `IReadOnlyList<string> Tags`
- `DateTimeOffset CreatedAt`
- `DateTimeOffset UpdatedAt`
- `DateTimeOffset? ScheduledAt`
- `DateTimeOffset? PublishedAt`

#### ContentDetailDto.cs

**Path:** `src/PBA.Application/Features/Content/Dtos/ContentDetailDto.cs`

Editor-view DTO. Includes everything in `ContentDto` plus body, metadata, and related collections.

Properties (in addition to all `ContentDto` fields):
- `string Body`
- `decimal? ViralityPrediction`
- `Guid? SourceIdeaId`
- `Guid? ParentContentId`
- `IReadOnlyList<PlatformPublishDto> PlatformPublishes` -- records of WHERE content was published
- `IReadOnlyList<ChildContentDto> Children` -- platform-specific ADAPTATIONS (separate Content entities)

Important design note: `PlatformPublishes` maps from `Content.CrossPosts` (the `IReadOnlyList<ContentPlatformPublish>` navigation). `Children` maps from the `Content.Children` navigation (self-referential via `ParentContentId`). These are two distinct concepts -- do not conflate them.

#### PlatformPublishDto.cs

**Path:** `src/PBA.Application/Features/Content/Dtos/PlatformPublishDto.cs`

Maps from `ContentPlatformPublish` entity.

Properties:
- `Guid Id`
- `Platform Platform`
- `PublishStatus PublishStatus`
- `string? PublishedUrl`
- `DateTimeOffset? PublishedAt`

#### ChildContentDto.cs

**Path:** `src/PBA.Application/Features/Content/Dtos/ChildContentDto.cs`

Lightweight summary of a child Content entity.

Properties:
- `Guid Id`
- `string Title`
- `ContentType ContentType`
- `Platform PrimaryPlatform`
- `ContentStatus Status`
- `DateTimeOffset UpdatedAt`

#### VoiceCheckDto.cs

**Path:** `src/PBA.Application/Features/Content/Dtos/VoiceCheckDto.cs`

Returned by the CheckVoice query handler.

Properties:
- `decimal Score` -- 0-100 scale
- `string Feedback` -- AI explanation of the score

### Request DTOs

All request DTOs go in: `src/PBA.Application/Features/Content/Dtos/`

#### CreateContentRequest.cs

Properties:
- `string Title` (required, 1-200 chars)
- `ContentType ContentType` (required, valid enum)
- `Platform PrimaryPlatform` (required, valid enum)
- `Guid? SourceIdeaId` (optional -- when provided, CreateContent delegates to CreateContentFromIdea logic)
- `IReadOnlyList<string> Tags` (optional, default empty)

#### UpdateContentRequest.cs

Properties:
- `string? Title` (optional, 1-200 chars when provided)
- `string? Body` (optional, max 100,000 chars)
- `IReadOnlyList<string>? Tags` (optional)
- `ContentType? ContentType` (optional)
- `Platform? PrimaryPlatform` (optional)
- `DateTimeOffset LastUpdatedAt` (required -- server rejects if it does not match `Content.UpdatedAt` for optimistic concurrency)

Null means "no change" -- only non-null fields are applied.

#### DraftContentRequest.cs

Properties:
- `string Action` (required -- one of: "draft", "refine", "shorten", "expand", "changeTone")
- `string? Instructions` (optional, max 2000 chars -- additional user guidance)
- `string? ToneName` (optional -- only relevant for "changeTone" action)

#### ScheduleContentRequest.cs

Properties:
- `DateTimeOffset ScheduledAt` (required, must be in future)

#### CrossPostRequest.cs

Properties:
- `Platform TargetPlatform` (required, valid enum -- must be different from content's PrimaryPlatform, but that check happens in the command handler since it requires loading the entity)

### Validators

All validators go in: `src/PBA.Application/Features/Content/Validators/`

Validators are auto-discovered via assembly scanning (already configured in the Application DI layer from Step 2). They are applied through the existing MediatR `ValidationBehavior<TRequest, TResponse>` pipeline behavior.

**Recommended approach:** Follow the existing pattern where validators target `AbstractValidator<SomeCommand.Command>`. The command records embed request DTO properties directly. This keeps validators discoverable by the MediatR pipeline.

#### CreateContentRequestValidator.cs

Rules:
- `Title`: NotEmpty, MaximumLength(200)
- `ContentType`: IsInEnum()
- `PrimaryPlatform`: IsInEnum()

#### UpdateContentRequestValidator.cs

Rules:
- `Body`: MaximumLength(100_000) when provided (`.When(x => x.Body is not null)`)
- `Title`: MaximumLength(200) when provided (`.When(x => x.Title is not null)`)
- `LastUpdatedAt`: NotEqual(default(DateTimeOffset)) -- ensures it was actually set

#### DraftContentRequestValidator.cs

Rules:
- `Action`: NotEmpty, Must be one of `["draft", "refine", "shorten", "expand", "changeTone"]`
- `Instructions`: MaximumLength(2000) when provided

Implementation note for the Action validation:
```csharp
private static readonly HashSet<string> ValidActions = ["draft", "refine", "shorten", "expand", "changeTone"];

RuleFor(x => x.Action)
    .NotEmpty()
    .Must(a => ValidActions.Contains(a))
    .WithMessage("Action must be one of: draft, refine, shorten, expand, changeTone");
```

#### ScheduleContentRequestValidator.cs

Rules:
- `ScheduledAt`: GreaterThan(DateTimeOffset.UtcNow) with message "ScheduledAt must be in the future"

#### CrossPostRequestValidator.cs

Rules:
- `TargetPlatform`: IsInEnum()

---

## File Summary

### New Files (20 total)

**Response DTOs (5 files):**
- `src/PBA.Application/Features/Content/Dtos/ContentDto.cs`
- `src/PBA.Application/Features/Content/Dtos/ContentDetailDto.cs`
- `src/PBA.Application/Features/Content/Dtos/PlatformPublishDto.cs`
- `src/PBA.Application/Features/Content/Dtos/ChildContentDto.cs`
- `src/PBA.Application/Features/Content/Dtos/VoiceCheckDto.cs`

**Request DTOs (5 files):**
- `src/PBA.Application/Features/Content/Dtos/CreateContentRequest.cs`
- `src/PBA.Application/Features/Content/Dtos/UpdateContentRequest.cs`
- `src/PBA.Application/Features/Content/Dtos/DraftContentRequest.cs`
- `src/PBA.Application/Features/Content/Dtos/ScheduleContentRequest.cs`
- `src/PBA.Application/Features/Content/Dtos/CrossPostRequest.cs`

**Validators (5 files):**
- `src/PBA.Application/Features/Content/Validators/CreateContentRequestValidator.cs`
- `src/PBA.Application/Features/Content/Validators/UpdateContentRequestValidator.cs`
- `src/PBA.Application/Features/Content/Validators/DraftContentRequestValidator.cs`
- `src/PBA.Application/Features/Content/Validators/ScheduleContentRequestValidator.cs`
- `src/PBA.Application/Features/Content/Validators/CrossPostRequestValidator.cs`

**Test Files (5 files):**
- `tests/PBA.Application.Tests/Features/Content/Validators/CreateContentRequestValidatorTests.cs`
- `tests/PBA.Application.Tests/Features/Content/Validators/UpdateContentRequestValidatorTests.cs`
- `tests/PBA.Application.Tests/Features/Content/Validators/DraftContentRequestValidatorTests.cs`
- `tests/PBA.Application.Tests/Features/Content/Validators/ScheduleContentRequestValidatorTests.cs`
- `tests/PBA.Application.Tests/Features/Content/Validators/CrossPostRequestValidatorTests.cs`

### Modified Files

- `src/PBA.Application/Features/Ideas/Commands/CreateContentFromIdea.cs` -- namespace collision fix: `using ContentEntity = PBA.Domain.Entities.Content;` alias to disambiguate from `PBA.Application.Features.Content` namespace

---

## Implementation Notes

1. **Record pattern:** All DTOs use `record` with `init`-only properties, matching the existing `IdeaDto`/`CreateIdeaRequest` pattern in the codebase.

2. **Namespace convention:** `PBA.Application.Features.Content.Dtos` for DTOs, `PBA.Application.Features.Content.Validators` for validators. This matches the Idea Bank structure under `PBA.Application.Features.Ideas.*`.

3. **Immutable collections:** Use `IReadOnlyList<T>` on all public DTO surfaces. Default empty collections to `[]` where applicable.

4. **Validator discovery:** The existing `DependencyInjection.cs` in the Application project registers validators via `services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly)`. No additional registration needed -- new validators are auto-discovered.

5. **Validator target type:** Validators currently target request DTOs directly (`AbstractValidator<CreateContentRequest>`). Section-05 will re-target them to MediatR Command types when commands are built.

6. **Enum validation:** Use FluentValidation's built-in `.IsInEnum()` for `ContentType` and `Platform`. This rejects values not defined in the enum, including cast-from-int values like `(ContentType)999`.

## Deviations from Plan

1. **Namespace collision fix:** Creating `PBA.Application.Features.Content` namespace shadowed the `Content` domain entity. Fixed `CreateContentFromIdea.cs` with a using alias (`ContentEntity`). The `ContentStateMachine` from section-02 already used fully-qualified paths.

2. **Additional validation rules (from code review):**
   - `UpdateContentRequestValidator`: Added `NotEmpty()` on Title, `IsInEnum().When(HasValue)` for nullable ContentType/Platform
   - `DraftContentRequestValidator`: Added `MaximumLength(200)` on ToneName
   - 5 additional test cases added (26 total, up from planned 21)

3. **Tracked for section-05:** `ScheduleContentRequestValidator` uses `DateTimeOffset.UtcNow` directly -- should inject `TimeProvider` when validators move to command types.

4. **Design decision:** Action strings in `DraftContentRequest` are case-sensitive by design (strict API contract).
