These properties do not exist yet -- they are added by section-01-foundation. Now I have everything I need to write the section.

# Section 05: Platform Formatter Changes (Image Passthrough)

## Overview

This section modifies the existing platform content formatters so they can pass image data through to the publishing adapters. Today every formatter hardcodes `Array.Empty<MediaFile>()` when constructing `PlatformContent`, which means even if a `Content` entity has an associated image, the adapter never sees it. This section establishes the image data flow from `Content` entity through `PlatformContent.Media` to the platform adapters.

**Scope:** `LinkedInContentFormatter`, `TwitterContentFormatter`, `RedditContentFormatter`, `YouTubeContentFormatter`, `InstagramContentFormatter`, the `IPlatformContentFormatter` interface, and the `MediaFile` record.

---

## Dependencies

- **section-01-foundation** must be completed first. It adds:
  - `Content.ImageFileId` (nullable string) -- the platform-specific cropped image reference
  - `Content.ImageRequired` (bool, default false) -- when true, formatter must reject content that has no image
  - EF Core migration for both columns
- **section-04-image-resizer** must be completed first. It stores resized images and populates `Content.ImageFileId` with per-platform file IDs via `IMediaStorage`.

---

## Problem Statement

The publishing pipeline calls `formatter.FormatAndValidate(content)` which returns a `PlatformContent`. The `PlatformContent` record already has an `IReadOnlyList<MediaFile> Media` field, and `MediaFile` already has `FileId`, `MimeType`, and `AltText` properties. But no formatter ever populates the media list.

After this section:
1. Formatters check `Content.ImageFileId`. If set, they load image metadata and construct a `MediaFile` to include in `PlatformContent.Media`.
2. Formatters check `Content.ImageRequired`. If true and `ImageFileId` is null, they return a validation error.
3. Alt text is read from `Content.Metadata.PlatformSpecificData["imageAltText"]`.

The formatter does NOT load image bytes. It passes the `FileId` reference. The adapter is responsible for loading bytes via `IMediaStorage` when it needs to upload the image to the platform API.

---

## Existing Code (Key Files)

### Interface: `IPlatformContentFormatter`
**Path:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IPlatformContentFormatter.cs`

```csharp
public interface IPlatformContentFormatter
{
    PlatformType Platform { get; }
    Result<PlatformContent> FormatAndValidate(Content content);
}
```

The interface signature does NOT change. The `Content` entity already carries all the data the formatters need (`ImageFileId`, `ImageRequired`, `Metadata.PlatformSpecificData`).

### Model: `PlatformContent` and `MediaFile`
**Path:** `src/PersonalBrandAssistant.Application/Common/Models/PlatformContent.cs`

```csharp
public record PlatformContent(
    string Text, string? Title, ContentType ContentType,
    IReadOnlyList<MediaFile> Media,
    IReadOnlyDictionary<string, string> Metadata);

public record MediaFile(string FileId, string MimeType, string? AltText);
```

These records are already correct. No changes needed.

### Content Entity Fields (added by section-01)
**Path:** `src/PersonalBrandAssistant.Domain/Entities/Content.cs`

After section-01, `Content` will have:
- `public string? ImageFileId { get; set; }` -- nullable, references media storage file ID for the platform-cropped image
- `public bool ImageRequired { get; set; }` -- default false, set to true by the orchestrator for automation-generated content

### ContentMetadata Convention
**Path:** `src/PersonalBrandAssistant.Domain/ValueObjects/ContentMetadata.cs`

Alt text is stored at `Metadata.PlatformSpecificData["imageAltText"]`. This key is populated by the orchestrator (section-08) when the `ImagePromptService` generates alt text alongside the image prompt.

---

## Tests First

All tests go in the existing test project at `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/`.

### LinkedInContentFormatterTests (additions)

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInContentFormatterTests.cs`

Add the following test methods to the existing `LinkedInContentFormatterTests` class:

- **Test: FormatAndValidate_WithImageFileId_IncludesMediaFile** -- Create a `Content` with `ImageFileId = "img-123"` and `Metadata.PlatformSpecificData["imageAltText"] = "Professional graphic about AI trends"`. Call `FormatAndValidate`. Assert `result.Value.Media` has exactly one entry with `FileId == "img-123"`, `MimeType == "image/png"`, and the correct `AltText`.

- **Test: FormatAndValidate_WithImageFileIdButNoAltText_IncludesMediaFileWithNullAlt** -- Create a `Content` with `ImageFileId = "img-456"` but no `imageAltText` key in `PlatformSpecificData`. Assert `result.Value.Media` has one entry with `AltText == null`.

- **Test: FormatAndValidate_NoImageFileId_ExcludesMediaFile** -- Create a `Content` with `ImageFileId = null`. Assert `result.Value.Media` is empty. This validates backward compatibility.

- **Test: FormatAndValidate_ImageRequired_True_NoImage_ReturnsValidationError** -- Create a `Content` with `ImageRequired = true` and `ImageFileId = null`. Assert `result.IsSuccess` is false and errors contain a message about missing required image.

- **Test: FormatAndValidate_ImageRequired_True_WithImage_Succeeds** -- Create a `Content` with `ImageRequired = true` and `ImageFileId = "img-789"`. Assert `result.IsSuccess` is true and `Media` has one entry.

- **Test: FormatAndValidate_ImageRequired_False_NoImage_Succeeds** -- Create a `Content` with `ImageRequired = false` and `ImageFileId = null`. Assert `result.IsSuccess` is true (backward compatible).

### TwitterContentFormatterTests (additions)

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterContentFormatterTests.cs`

Add the same pattern of tests to `TwitterContentFormatterTests`:

- **Test: FormatAndValidate_WithImageFileId_IncludesMediaFile** -- Same pattern as LinkedIn. Assert single `MediaFile` in `Media` list.

- **Test: FormatAndValidate_NoImageFileId_ExcludesMediaFile** -- Assert `Media` is empty for backward compatibility.

- **Test: FormatAndValidate_ImageRequired_True_NoImage_ReturnsValidationError** -- Assert validation failure.

- **Test: FormatAndValidate_Thread_WithImage_FirstTweetHasMedia** -- Create long content that splits into a thread plus `ImageFileId`. Assert `Media` contains the image (images attach to the first tweet in a thread).

### Other Formatter Tests

Apply the same ImageRequired validation test to `RedditContentFormatterTests`, `YouTubeContentFormatterTests`, and `InstagramContentFormatterTests`. The image passthrough tests may be simpler since some platforms handle images differently, but the `ImageRequired` guard applies universally.

---

## Implementation Details

### Strategy: Extract Shared Image Logic into FormatterHelpers

Rather than duplicating image construction logic across five formatters, add a helper method to `FormatterHelpers`.

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/FormatterHelpers.cs`

Add two new methods:

1. **`BuildMediaList(Content content)`** -- Returns `IReadOnlyList<MediaFile>`. If `content.ImageFileId` is not null, creates a single-element list with `new MediaFile(content.ImageFileId, "image/png", altText)` where `altText` comes from `content.Metadata.PlatformSpecificData.GetValueOrDefault("imageAltText")`. If `ImageFileId` is null, returns `Array.Empty<MediaFile>()`.

2. **`ValidateImageRequirement(Content content)`** -- Returns `string?` (null if valid, error message if invalid). If `content.ImageRequired` is true and `content.ImageFileId` is null or whitespace, returns `"{Platform} post requires an image but none is attached"`. Otherwise returns null.

### LinkedInContentFormatter Changes

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/LinkedInContentFormatter.cs`

Modify `FormatAndValidate`:
1. After the empty body check, add the image requirement validation call. If it returns an error string, return `Result.ValidationFailure<PlatformContent>([error])`.
2. Replace `Array.Empty<MediaFile>()` in the `PlatformContent` constructor with `FormatterHelpers.BuildMediaList(content)`.

The constructor does NOT need `IMediaStorage` injection. The formatter only passes the file ID reference, not the bytes.

### TwitterContentFormatter Changes

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/TwitterContentFormatter.cs`

Same two changes:
1. Add image requirement validation after the empty body check.
2. Replace `Array.Empty<MediaFile>()` in both the single-tweet path and the `BuildThread` method's `PlatformContent` constructor with `FormatterHelpers.BuildMediaList(content)`.

Note: For threads, the `Content` parameter is already passed into `BuildThread`, so the helper can be called there. The image attaches to the first tweet; Twitter's API handles media attachment at the tweet level, so the formatter just passes it through and the adapter decides how to use it.

### RedditContentFormatter Changes

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/RedditContentFormatter.cs`

Same pattern:
1. Add image requirement validation.
2. Replace `Array.Empty<MediaFile>()` with `FormatterHelpers.BuildMediaList(content)`.

### YouTubeContentFormatter Changes

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/YouTubeContentFormatter.cs`

Same pattern. For YouTube, the image would serve as a custom thumbnail.

### InstagramContentFormatter Changes

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/InstagramContentFormatter.cs`

This formatter already validates media presence via `HasMedia()` which checks `Metadata.PlatformSpecificData["media_count"]`. The `ImageFileId` / `BuildMediaList` path is an additional, parallel mechanism for automation-generated content. Apply:
1. Image requirement validation.
2. Replace `Array.Empty<MediaFile>()` with `FormatterHelpers.BuildMediaList(content)`.

The existing `HasMedia` check remains for manually-created Instagram content that uses the `media_count` metadata convention.

---

## Data Flow Summary

After this section, the end-to-end image data flow is:

1. **Orchestrator** (section-08) stores `imageFileId` on each child `Content` entity after `ImageResizer` produces platform-specific crops.
2. **Orchestrator** sets `content.Metadata.PlatformSpecificData["imageAltText"]` with the AI-generated alt text.
3. **Orchestrator** sets `content.ImageRequired = true`.
4. **PublishingPipeline** calls `formatter.FormatAndValidate(content)`.
5. **Formatter** (this section) reads `Content.ImageFileId`, constructs `MediaFile(fileId, "image/png", altText)`, includes it in `PlatformContent.Media`. Also validates `ImageRequired`.
6. **PublishingPipeline** passes `PlatformContent` to `adapter.PublishAsync(platformContent, ct)`.
7. **Adapter** (section-06 for LinkedIn) reads `PlatformContent.Media`, loads bytes via `IMediaStorage.GetStreamAsync(fileId)`, uploads to the platform API.

---

## Files Modified

| File | Change |
|------|--------|
| `src/.../Formatters/FormatterHelpers.cs` | Add `BuildMediaList()` and `ValidateImageRequirement()` |
| `src/.../Formatters/LinkedInContentFormatter.cs` | Use `BuildMediaList`, add `ValidateImageRequirement` |
| `src/.../Formatters/TwitterContentFormatter.cs` | Use `BuildMediaList`, add `ValidateImageRequirement` |
| `src/.../Formatters/RedditContentFormatter.cs` | Use `BuildMediaList`, add `ValidateImageRequirement` |
| `src/.../Formatters/YouTubeContentFormatter.cs` | Use `BuildMediaList`, add `ValidateImageRequirement` |
| `src/.../Formatters/InstagramContentFormatter.cs` | Use `BuildMediaList`, add `ValidateImageRequirement` |
| `tests/.../Platform/LinkedInContentFormatterTests.cs` | Add 6 new test methods |
| `tests/.../Platform/TwitterContentFormatterTests.cs` | Add 4 new test methods |
| `tests/.../Platform/RedditContentFormatterTests.cs` | Add ImageRequired tests |
| `tests/.../Platform/YouTubeContentFormatterTests.cs` | Add ImageRequired tests |
| `tests/.../Platform/InstagramContentFormatterTests.cs` | Add ImageRequired tests |

## Files NOT Modified

- `IPlatformContentFormatter.cs` -- interface signature unchanged
- `PlatformContent.cs` / `MediaFile.cs` -- record definitions unchanged
- `IMediaStorage.cs` -- not injected into formatters; adapters handle byte loading
- `PublishingPipeline.cs` -- already passes `PlatformContent` with its `Media` list to adapters; no changes needed
- `LinkedInPlatformAdapter.cs` -- image upload is section-06's responsibility

---

## Verification

After implementation, run:
```bash
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests --filter "FullyQualifiedName~ContentFormatterTests"
```

All existing formatter tests must continue to pass (backward compatibility). All new image-related tests must pass. The key invariant: when `ImageFileId` is null and `ImageRequired` is false, formatter behavior is identical to pre-change behavior.