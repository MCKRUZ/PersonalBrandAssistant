# Section 07: Content Formatters

## Overview

This section implements per-platform content formatters that transform a `Content` domain entity into platform-specific `PlatformContent` records. Each formatter applies platform character limits, hashtag rules, media requirements, and structural formatting (e.g., Twitter thread splitting). All formatters implement the `IPlatformContentFormatter` interface with a combined `FormatAndValidate` method that returns `Result<PlatformContent>`.

## Dependencies

- **Section 01 (Domain Entities):** `Content` entity, `ContentType` enum, `PlatformType` enum
- **Section 02 (Interfaces & Models):** `IPlatformContentFormatter` interface, `PlatformContent` record, `MediaFile` record

## Platform-Specific Formatting Rules

| Platform | Char Limit | Hashtags | Media | Special |
|----------|-----------|----------|-------|---------|
| Twitter/X | 280 chars | Inline or appended within limit | Images, video, GIFs | Thread splitting for long content |
| LinkedIn | 3,000 chars | Inline (preserved) | Images, video, documents | Article support |
| Instagram | 2,200 chars caption | Appended (max 30) | Required (no text-only) | Carousel up to 10 items |
| YouTube | 5,000 chars description | Tags as separate field | Thumbnail | Title required (100 chars max) |

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/TwitterContentFormatter.cs` | Twitter formatting + thread splitting |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/LinkedInContentFormatter.cs` | LinkedIn formatting |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/InstagramContentFormatter.cs` | Instagram formatting (media required) |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/YouTubeContentFormatter.cs` | YouTube formatting (title required) |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/FormatterHelpers.cs` | Shared SafeTruncate + ReadOnlyDictionary helpers |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterContentFormatterTests.cs` | 10 tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInContentFormatterTests.cs` | 9 tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramContentFormatterTests.cs` | 10 tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubeContentFormatterTests.cs` | 8 tests |

## Tests (Write First)

Formatters are pure functions -- no mocking needed. Create `Content` instances directly.

### TwitterContentFormatterTests

```csharp
// Test: FormatAndValidate truncates text to 280 chars with ellipsis
//   Arrange: Content with body of exactly 285 chars, no tags
//   Assert: result.Value.Text.Length <= 280, ends with "..."

// Test: FormatAndValidate splits long content into thread
//   Arrange: Content with body of ~600 chars
//   Assert: result.Value.Metadata contains "thread:1" key (at minimum)
//   Each thread part <= 280 chars

// Test: Thread splits at sentence boundaries, not mid-word
//   Arrange: Multiple sentences
//   Assert: no tweet ends mid-word, splits after sentence-ending punctuation

// Test: Thread adds numbering (1/N format)
//   Arrange: Content requiring 3-part thread
//   Assert: first tweet ends with "1/3", second "2/3", third "3/3"

// Test: FormatAndValidate appends hashtags within char limit
//   Arrange: Short body + tags
//   Assert: hashtags appended, total <= 280

// Test: FormatAndValidate returns failure for empty text
//   Arrange: Content with empty body
//   Assert: result.IsSuccess == false
```

### LinkedInContentFormatterTests

```csharp
// Test: FormatAndValidate allows up to 3000 chars
//   Arrange: Content with 2000-char body
//   Assert: success

// Test: FormatAndValidate returns failure for text exceeding 3000 chars
//   Arrange: Content with 3500-char body
//   Assert: validation failure

// Test: FormatAndValidate preserves inline hashtags
//   Arrange: Body contains "#leadership" inline
//   Assert: preserved in output text
```

### InstagramContentFormatterTests

```csharp
// Test: FormatAndValidate returns failure when no media attached
//   Arrange: Content without media indicators in PlatformSpecificData
//   Assert: failure with "Instagram requires at least one media attachment"

// Test: FormatAndValidate limits caption to 2200 chars
//   Arrange: Content with 2500-char body
//   Assert: truncated to 2200 with "..."

// Test: FormatAndValidate limits hashtags to 30
//   Arrange: 35 tags
//   Assert: only 30 appended

// Test: FormatAndValidate limits carousel to 10 items
//   Arrange: carousel_count=12 in PlatformSpecificData
//   Assert: failure
```

### YouTubeContentFormatterTests

```csharp
// Test: FormatAndValidate requires Title
//   Arrange: Content with null title
//   Assert: failure

// Test: FormatAndValidate limits title to 100 chars
//   Arrange: 150-char title
//   Assert: truncated to 100 with "..."

// Test: FormatAndValidate limits description to 5000 chars
//   Arrange: 6000-char body
//   Assert: truncated to 5000 with "..."

// Test: FormatAndValidate separates tags from description
//   Arrange: Tags in Metadata.Tags
//   Assert: stored in Metadata["tags"] as comma-separated, not in Text
```

## Implementation Details

### TwitterContentFormatter

Implements `IPlatformContentFormatter` with `Platform => PlatformType.TwitterX`.

Behavior:
- Empty/whitespace body -> `ValidationFailure`
- Append hashtags from `content.Metadata.Tags` within 280-char limit
- If fits in 280 chars -> single `PlatformContent`
- If exceeds 280 -> split into thread:
  - Split at sentence boundaries (period/exclamation/question + space)
  - Each tweet max 280 chars including numbering
  - Add `1/N` format at end of each tweet
  - Store thread parts in `Metadata` with keys `thread:0`, `thread:1`, etc.
  - `Text` holds the first tweet
  - Hashtags on last tweet if they fit
- Single word exceeding limit -> truncate with `...`

### LinkedInContentFormatter

Implements `IPlatformContentFormatter` with `Platform => PlatformType.LinkedIn`.

- Empty/whitespace body -> `ValidationFailure`
- Preserve inline hashtags already in body
- Append additional hashtags from `Metadata.Tags` not already inline
- Text exceeds 3,000 chars after hashtags -> `ValidationFailure` (do not truncate)
- Preserve `Title` from content if present

### InstagramContentFormatter

Implements `IPlatformContentFormatter` with `Platform => PlatformType.Instagram`.

- Check media presence via `ContentType` or `PlatformSpecificData["media_count"]`
- No media -> `ValidationFailure`
- Truncate caption to 2,200 with `...` if exceeded
- Limit hashtags to 30 (take first 30, log warning if more)
- `carousel_count > 10` in PlatformSpecificData -> `ValidationFailure`
- Append hashtags at end, separated by blank line

### YouTubeContentFormatter

Implements `IPlatformContentFormatter` with `Platform => PlatformType.YouTube`.

- Null/whitespace title -> `ValidationFailure`
- Title > 100 chars -> truncate with `...`
- Body (description) > 5,000 chars -> truncate with `...`
- Tags from `Metadata.Tags` -> store in `Metadata["tags"]` as comma-separated
- Return with `Title` set and `Text` as description

### Notes

- Use ASCII `...` for truncation (safer for platform compatibility)
- Thread numbering: `" 1/3"` appended at end, counted in 280-char budget
- Instagram media validation is lightweight at formatter level -- pipeline does actual attachment
- All formatters are scoped services, resolved via `IEnumerable<IPlatformContentFormatter>`
- DI registration in Section 12

## Implementation Deviations

### Path Change
- Plan used `Services/Platform/Formatters/` but actual path is `Services/PlatformServices/Formatters/` (namespace collision fix from section-06)

### ContentType Enum Mismatch
- Plan referenced `ContentType.Image`, `ContentType.Video`, `ContentType.Carousel` which don't exist
- Actual enum: `BlogPost`, `SocialPost`, `Thread`, `VideoDescription`
- Instagram media detection uses `PlatformSpecificData["media_count"]` exclusively (not ContentType)
- YouTube tests use `ContentType.VideoDescription`

### Code Review Fixes
- **Surrogate-safe truncation:** Added `FormatterHelpers.SafeTruncate()` that avoids slicing mid-surrogate pair for emoji-heavy content
- **Dynamic thread numbering:** Twitter thread suffix dynamically computes ` N/M` width based on actual part count (was fixed 6-char estimate)
- **LinkedIn hashtag dedup:** Fixed `TrimStart('#')` normalization to prevent `##tag` false negative
- **ReadOnlyDictionary:** All formatters return `ReadOnlyDictionary<string, string>` via `FormatterHelpers.ToReadOnly()`

### Final Test Count: 37 passing
