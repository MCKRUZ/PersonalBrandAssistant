# Section 03: Substack Prep Service

## Overview

Implements `ISubstackPrepService` -- transforms finalized blog content into Substack-optimized fields (title, subtitle, body markdown, SEO description, tags, section, preview text) for manual copy-paste. Also implements manual publish tracking with idempotency.

**Depends on:** Section 01 (Content.SubstackPostUrl, SubstackDetection entity)
**Blocks:** Section 10 (Substack Prep UI)

---

## Tests (Write First)

### SubstackPrepService Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/SubstackPrepServiceTests.cs`

```csharp
// Test: PrepareAsync generates all Substack fields from finalized content
// Test: PrepareAsync extracts subtitle from first paragraph
// Test: PrepareAsync truncates preview text at 200 chars on word boundary
// Test: PrepareAsync produces clean markdown (no raw HTML in body)
// Test: PrepareAsync extracts tags from content metadata
// Test: PrepareAsync derives section name from content series/category
// Test: PrepareAsync returns null canonical URL when blog not yet published
// Test: PrepareAsync returns failure for non-existent content
// Test: PrepareAsync handles content with no metadata gracefully
// Test: MarkPublishedAsync sets SubstackPostUrl on content
// Test: MarkPublishedAsync creates SubstackDetection record
// Test: MarkPublishedAsync triggers notification for blog scheduling
// Test: MarkPublishedAsync is idempotent (no-op if already published)
// Test: MarkPublishedAsync with URL takes precedence over RSS detection
```

### Endpoint Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/SubstackPrepEndpointsTests.cs`

```csharp
// Test: GET /api/content/{id}/substack-prep returns formatted fields
// Test: GET /api/content/{id}/substack-prep returns 404 for non-existent content
// Test: POST /api/content/{id}/substack-published sets SubstackPostUrl
// Test: POST /api/content/{id}/substack-published is idempotent
// Test: POST /api/content/{id}/substack-published returns 404 for non-existent
```

---

## Implementation Details

### Interface
File: `src/PersonalBrandAssistant.Application/Common/Interfaces/ISubstackPrepService.cs`

```csharp
public interface ISubstackPrepService
{
    Task<Result<SubstackPreparedContent>> PrepareAsync(Guid contentId, CancellationToken ct);
    Task<Result<SubstackPublishConfirmation>> MarkPublishedAsync(Guid contentId, string? substackUrl, CancellationToken ct);
}
```

### DTOs
File: `src/PersonalBrandAssistant.Application/Common/Models/SubstackPreparedContent.cs`

```csharp
public record SubstackPreparedContent(string Title, string Subtitle, string Body, string SeoDescription, string[] Tags, string? SectionName, string PreviewText, string? CanonicalUrl);
public record SubstackPublishConfirmation(Guid ContentId, string? SubstackPostUrl, bool WasAlreadyPublished);
```

### Service Implementation
File: `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/SubstackPrepService.cs`

**PrepareAsync**: Load content, extract subtitle from first paragraph, strip raw HTML from body, demote headings (# → ## for Substack), extract tags from metadata, truncate preview at 200 chars on word boundary.

**MarkPublishedAsync**: Idempotency check (return early if SubstackPostUrl already set), set SubstackPostUrl, update ContentPlatformStatus for Substack to Published, create SubstackDetection record (High confidence for manual), trigger notification if PersonalBlog in TargetPlatforms.

**Race condition handling**: Unique index on SubstackDetection.SubstackUrl prevents duplicates. If RSS detects later, it skips already-linked content.

### MarkdownSanitizer Utility
File: `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/MarkdownSanitizer.cs`

Static helper: `StripHtml()`, `DemoteHeadings()`, `ToPlainText()`, `TruncateAtWordBoundary()`.

### Endpoints
File: `src/PersonalBrandAssistant.Api/Endpoints/SubstackPrepEndpoints.cs`

```
GET  /api/content/{id}/substack-prep       → SubstackPreparedContent
POST /api/content/{id}/substack-published   → SubstackPublishConfirmation (body: { substackUrl? })
```

---

## Files
| File | Action |
|------|--------|
| `Application/Common/Interfaces/ISubstackPrepService.cs` | Create |
| `Application/Common/Models/SubstackPreparedContent.cs` | Create |
| `Infrastructure/Services/ContentServices/SubstackPrepService.cs` | Create |
| `Infrastructure/Services/ContentServices/MarkdownSanitizer.cs` | Create |
| `Api/Endpoints/SubstackPrepEndpoints.cs` | Create |
| `Infrastructure/DependencyInjection.cs` | Modify |
| `Api/Program.cs` | Modify |

---

## Implementation Notes (Actual)

**Status:** COMPLETE
**Tests:** 12 passing
**Test file:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/SubstackPrepServiceTests.cs`

**Deviations from plan:**
- ContentMetadata has no Description/Category fields; SEO description derived from plain text truncation, section name left null
- Endpoint tests deferred (require CustomWebApplicationFactory setup)
- MarkdownSanitizer uses source-generated regex for performance
