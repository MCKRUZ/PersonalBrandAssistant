# Section 04: Blog HTML Generator

## Overview

Implements `IBlogHtmlGenerator` -- converts finalized markdown content into matthewkruczek.ai HTML blog posts. Handles markdown-to-HTML via Markdig (raw HTML disabled for XSS prevention), slug generation with content ID hash suffix, canonical URL injection (placeholder until Substack URL known), Open Graph tags, and meta descriptions.

**Depends on:** Section 01 (Content.SubstackPostUrl, BlogPublishOptions)
**Blocks:** Section 05 (GitHub Publish), Section 11 (Blog Publish UI)

---

## Tests (Write First)

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogHtmlGeneratorTests.cs`

```csharp
// Test: GenerateAsync renders content into HTML template
// Test: GenerateAsync injects title, date, author, meta description
// Test: GenerateAsync injects Open Graph tags
// Test: GenerateAsync sets canonical URL to Substack URL when available
// Test: GenerateAsync uses placeholder canonical URL when Substack URL not yet known
// Test: GenerateAsync converts markdown to HTML with raw HTML disabled (XSS prevention)
// Test: GenerateAsync produces correct file path: blog/YYYY-MM-DD-slug-hash.html
// Test: GenerateAsync appends content ID hash suffix for uniqueness
// Test: GenerateAsync handles special characters in title for slug generation
// Test: GenerateAsync regenerates with updated canonical URL when called again

// Slug tests:
// "Hello World" → "hello-world"
// "Agent-First Enterprise: Part 5" → "agent-first-enterprise-part-5"
// "What's Next? AI & the Future" → "whats-next-ai-the-future"
// "" or null → "untitled"
// "Multiple---Dashes" → "multiple-dashes"
```

---

## Implementation Details

### NuGet: Add Markdig
Add `<PackageReference Include="Markdig" Version="0.38.0" />` to Infrastructure.csproj.

### Interface
File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IBlogHtmlGenerator.cs`
```csharp
public interface IBlogHtmlGenerator { Task<BlogHtmlResult> GenerateAsync(Guid contentId, CancellationToken ct); }
```

### Result Record
File: `src/PersonalBrandAssistant.Application/Common/Models/BlogHtmlResult.cs`
```csharp
public record BlogHtmlResult(string Html, string FilePath, string? CanonicalUrl);
```

### Service
File: `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/BlogHtmlGenerator.cs`

**Markdig pipeline** (static, thread-safe): `UseAdvancedExtensions().DisableHtml().Build()`

**Slug generation**: Lowercase, strip non-alphanumeric except hyphens, collapse consecutive hyphens, trim. Fallback "untitled" for null/empty.

**File path**: `{ContentPath}{CreatedAt:yyyy-MM-dd}-{slug}-{first6ofId}.html` -- deterministic across regenerations.

**Template**: Load from `BlogPublishOptions.TemplatePath`, replace `{{title}}`, `{{date}}`, `{{date_iso}}`, `{{author}}`, `{{meta_description}}`, `{{canonical_url}}`, `{{og_title}}`, `{{og_description}}`, `{{og_url}}`, `{{body}}`. Hardcoded fallback template if file missing.

**Canonical URL**: If `SubstackPostUrl` set, use it. If null, inject empty href (signals regeneration needed before publish).

### Template File
File: `src/PersonalBrandAssistant.Infrastructure/templates/blog-post.html` -- mark as `Content` with `CopyToOutputDirectory=PreserveNewest`.

---

## Files
| File | Action |
|------|--------|
| `Application/Common/Interfaces/IBlogHtmlGenerator.cs` | Create |
| `Application/Common/Models/BlogHtmlResult.cs` | Create |
| `Infrastructure/Services/ContentServices/BlogHtmlGenerator.cs` | Create |
| `Infrastructure/templates/blog-post.html` | Create |
| `Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` | Modify (Markdig + template) |
| `Infrastructure/DependencyInjection.cs` | Modify |
