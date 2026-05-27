# Section 03: Content Transformation

## Overview

This section implements the content transformation pipeline: a shared `ContentTransformer` that preprocesses raw markdown and delegates to platform-specific `IPlatformFormatter` implementations. This section covers the shared preprocessor logic and the `BlogFormatter` (the first formatter, converting markdown to HTML via Markdig with template application). Additional platform formatters (Medium, LinkedIn, Twitter, Substack) are implemented in their respective connector sections (07-10).

## Dependencies

- **Section 01 (Interfaces and Types):** Provides `IContentTransformer`, `IPlatformFormatter`, and `PreprocessedContent`/`ImageReference` record types in the Application layer. These interfaces must exist before this section can be implemented.

## Architecture

The transformation pipeline runs in two stages:

```
Raw Markdown (Content.Body)
    |
    v
ContentTransformer.TransformAsync(content, platform)
    |
    +-- SharedPreprocessor: strip YAML frontmatter, normalize image paths
    |
    +-- IPlatformFormatter (resolved by Platform enum via keyed DI)
    |   +-- BlogFormatter: markdown -> HTML via Markdig, apply template
    |   +-- (MediumFormatter, LinkedInFormatter, etc. -- other sections)
    |
    +-- Returns: string (formatted content ready for the platform API)
```

`ContentTransformer` is registered as `IContentTransformer` in the DI container. Each `IPlatformFormatter` is registered via keyed DI using the `Platform` enum, identical to the connector pattern.

## Files to Create/Modify

### New Files (Actual)

| File | Layer | Purpose |
|------|-------|---------|
| `src/PBA.Infrastructure/Transformers/ContentTransformer.cs` | Infrastructure | Shared preprocessor + formatter delegation |
| `src/PBA.Infrastructure/Transformers/BlogFormatter.cs` | Infrastructure | Markdown-to-HTML conversion via Markdig, template rendering |
| `src/PBA.Infrastructure/Transformers/TransformerOptions.cs` | Infrastructure | Decoupled options for ContentTransformer (BaseUrl only) |
| `tests/PBA.Infrastructure.Tests/Transformers/ContentTransformerTests.cs` | Tests | 6 tests for preprocessing and formatter delegation |
| `tests/PBA.Infrastructure.Tests/Transformers/BlogFormatterTests.cs` | Tests | 7 tests for Markdig conversion, template rendering, XSS, dates |

### Modified Files

| File | Change |
|------|--------|
| `src/PBA.Application/Common/Models/PreprocessedContent.cs` | Added `ContentType` and `CreatedAt` optional parameters |

### Deviations from Plan

1. **TransformerOptions extracted**: Plan used `BlogConnectorOptions.BaseUrl` in ContentTransformer. Code review flagged coupling a generic transformer to blog-specific config. Created `TransformerOptions` with just `BaseUrl`.
2. **XSS prevention added**: `BlogFormatter` uses `WebUtility.HtmlEncode` on all non-Markdig template tokens. Original BlogConnector had same vulnerability -- fixed here rather than perpetuating.
3. **CreatedAt field added**: `PreprocessedContent` extended with `DateTimeOffset? CreatedAt` to preserve content creation date. BlogFormatter uses it for `{{date}}` token, falling back to `DateTimeOffset.UtcNow`.
4. **Frontmatter stripping improved**: Searches for `\n---` (line-start delimiter) instead of `---` at any position, preventing false matches on `---` inside YAML values.

The DI registration (`AddPublishingDependencies`) is handled in section 15. This section focuses solely on the transformer/formatter implementations.

---

## Tests FIRST

All tests go in `tests/PBA.Infrastructure.Tests/Transformers/`. The project already has xUnit + Moq configured.

### ContentTransformerTests.cs

```csharp
// File: tests/PBA.Infrastructure.Tests/Transformers/ContentTransformerTests.cs

namespace PBA.Infrastructure.Tests.Transformers;

public class ContentTransformerTests
{
    // Test: ContentTransformer_WithBlogPlatform_DelegatesToBlogFormatter
    //   Arrange: Register a mock IPlatformFormatter for Platform.Blog in a service provider.
    //            Create Content with a simple markdown body.
    //   Act: Call TransformAsync(content, Platform.Blog, ct).
    //   Assert: The mock formatter's FormatAsync was called exactly once.
    //           The returned string matches what the mock returned.

    // Test: ContentTransformer_WithUnknownPlatform_ThrowsNotSupportedException
    //   Arrange: Create a service provider with NO formatter registered for Platform.Reddit.
    //   Act/Assert: TransformAsync(content, Platform.Reddit, ct) throws NotSupportedException
    //              (or InvalidOperationException -- pick one, document it).

    // Test: ContentTransformer_StripsFrontmatter_BeforeDelegating
    //   Arrange: Content.Body = "---\ntitle: Hello\ntags: [a, b]\n---\n\nActual content here."
    //            Register a mock formatter that captures the PreprocessedContent argument.
    //   Act: Call TransformAsync.
    //   Assert: The PreprocessedContent.Body passed to the formatter does NOT contain "---"
    //           or "title: Hello". It starts with "Actual content here."

    // Test: ContentTransformer_ResolvesRelativeImagePaths_ToAbsoluteUrls
    //   Arrange: Content.Body contains "![alt](images/photo.png)" and "![alt2](/images/other.jpg)".
    //            BlogConnectorOptions.BaseUrl = "https://matthewkruczek.ai".
    //            Register a mock formatter that captures PreprocessedContent.
    //   Act: Call TransformAsync.
    //   Assert: PreprocessedContent.Images contains ImageReference entries with absolute URLs.
    //           PreprocessedContent.Body has image paths resolved to absolute URLs.

    // Test: ContentTransformer_PreservesContentMetadata_InPreprocessedContent
    //   Arrange: Content with Title="My Title", Tags=["AI","Tech"],
    //            PrimaryPlatform=Blog (for canonical URL construction).
    //   Act: Call TransformAsync.
    //   Assert: PreprocessedContent.Title == "My Title",
    //           PreprocessedContent.Tags contains "AI" and "Tech".

    // Test: ContentTransformer_EmptyBody_PassesEmptyToFormatter
    //   Arrange: Content with Body = "".
    //   Act: Call TransformAsync.
    //   Assert: Formatter receives PreprocessedContent with Body = "".
}
```

### BlogFormatterTests.cs

```csharp
// File: tests/PBA.Infrastructure.Tests/Transformers/BlogFormatterTests.cs

namespace PBA.Infrastructure.Tests.Transformers;

public class BlogFormatterTests : IDisposable
{
    // Setup: create temp directory, write a simple HTML template file with
    // {{title}}, {{content}}, {{date}}, {{author}}, {{tags}}, {{category}} tokens.
    // BlogFormatter needs BlogConnectorOptions (or its own options) for TemplatePath, Author, BaseUrl.

    // Test: BlogFormatter_ConvertsMarkdownToHtml_ViaMarkdig
    //   Arrange: PreprocessedContent with Body = "## Hello\n\nThis is **bold** text."
    //   Act: FormatAsync(preprocessedContent, ct)
    //   Assert: Result contains "<h2" and "Hello</h2>" and "<strong>bold</strong>".

    // Test: BlogFormatter_AppliesHtmlTemplate_WithTokenReplacement
    //   Arrange: PreprocessedContent with Title="Test", Tags=["AI","Dev"],
    //            Body = "Some content".
    //   Act: FormatAsync(preprocessedContent, ct)
    //   Assert: Result contains "<title>Test</title>", "AI, Dev", author name, date.

    // Test: BlogFormatter_GeneratesUrlSlug_FromTitle
    //   This test verifies the slug is available for the publish URL.
    //   Arrange: PreprocessedContent with Title = "My Amazing Blog Post"
    //   Act: FormatAsync
    //   Assert: The returned HTML (or a secondary output) includes slug "my-amazing-blog-post".
    //   NOTE: Slug generation already exists in BlogConnector.GenerateSlug (static method).
    //         BlogFormatter may not need its own slug logic -- it focuses on HTML content.
    //         This test may be deferred or adjusted based on whether slug ownership moves.

    // Test: BlogFormatter_HandlesSpecialCharactersInTitle_ForSlugGeneration
    //   Same note as above -- slug generation may remain on BlogConnector.

    // Test: BlogFormatter_HandlesEmptyBody_ProducesValidHtml
    //   Arrange: PreprocessedContent with Body = ""
    //   Act: FormatAsync
    //   Assert: Returns valid template with empty content area (no crash).

    // Test: BlogFormatter_HandlesCodeBlocks_InMarkdown
    //   Arrange: PreprocessedContent with Body containing fenced code block (```csharp ... ```)
    //   Act: FormatAsync
    //   Assert: Result contains <pre><code> elements.

    // Test: BlogFormatter_Platform_ReturnsBlog
    //   Assert: formatter.Platform == Platform.Blog

    // Cleanup: delete temp directory in Dispose().
}
```

---

## Implementation Details

### ContentTransformer

**File:** `src/PBA.Infrastructure/Transformers/ContentTransformer.cs`

**Responsibilities:**
1. Run shared preprocessing on the raw `Content.Body`
2. Resolve the correct `IPlatformFormatter` via keyed DI
3. Call the formatter and return its output

**Shared Preprocessing Steps:**

1. **Strip YAML frontmatter:** If `Content.Body` starts with `---`, find the closing `---` and remove everything between (inclusive). Use a simple regex or string scan -- this is a well-defined format.

2. **Resolve relative image paths:** Scan for markdown image syntax `![alt](path)`. For any path that doesn't start with `http://` or `https://`, resolve it against the blog's `BaseUrl` (from `BlogConnectorOptions` or a new `TransformerOptions`). Collect all images into `IReadOnlyList<ImageReference>` on the `PreprocessedContent` record.

3. **Build `PreprocessedContent`:** Assemble the record with:
   - `Title` from `Content.Title`
   - `Body` with frontmatter stripped and image paths resolved
   - `CanonicalUrl` constructed from blog base URL + slug (if content has been published to Blog)
   - `Tags` from `Content.Tags`
   - `Images` from the image scan

**Constructor dependencies:**
- `IServiceProvider` -- to resolve `IPlatformFormatter` via `GetKeyedService<IPlatformFormatter>(platform)`
- `IOptionsMonitor<BlogConnectorOptions>` -- for `BaseUrl` to resolve relative image paths (or a new dedicated options class if preferred)
- `ILogger<ContentTransformer>`

**Key design decisions:**
- If no formatter is registered for the requested platform, throw `NotSupportedException` with a clear message naming the platform.
- The preprocessor is a private method, not a separate class. It's simple enough to not warrant its own abstraction.
- `TransformAsync` is async because formatters may need async operations (e.g., file reads for templates, future API calls for image CDN uploads).

**Stub signature:**

```csharp
public sealed class ContentTransformer : IContentTransformer
{
    public async Task<string> TransformAsync(Content content, Platform platform, CancellationToken ct)
    {
        // 1. Preprocess: strip frontmatter, resolve images
        // 2. Resolve IPlatformFormatter from IServiceProvider by platform key
        // 3. Call formatter.FormatAsync(preprocessedContent, ct)
        // 4. Return formatted string
    }

    private PreprocessedContent Preprocess(Content content)
    {
        // Strip YAML frontmatter from Body
        // Scan for image references, resolve relative paths
        // Return PreprocessedContent record
    }

    private static string StripFrontmatter(string body)
    {
        // If body starts with "---\n", find next "---\n" and return everything after it
    }

    private (string body, IReadOnlyList<ImageReference> images) ResolveImagePaths(string body, string baseUrl)
    {
        // Regex scan for ![alt](path), resolve relative paths, collect ImageReference list
    }
}
```

### BlogFormatter

**File:** `src/PBA.Infrastructure/Transformers/BlogFormatter.cs`

**Responsibilities:**
1. Convert preprocessed markdown body to HTML using Markdig
2. Apply the HTML template with token replacement
3. Return the fully rendered HTML string

**This extracts the Markdig conversion logic currently embedded in `BlogConnector.PublishAsync`** (lines 43-51 of `BlogConnector.cs`). After this section, BlogConnector's migration (section 04) will remove the internal Markdig usage and consume `TransformedContent` from the publish request instead.

**Constructor dependencies:**
- `IOptionsMonitor<BlogConnectorOptions>` -- for `TemplatePath`, `Author`, `BaseUrl`
- `ILogger<BlogFormatter>`

**The Markdig pipeline** should match what BlogConnector currently uses: `new MarkdownPipelineBuilder().UseAdvancedExtensions().Build()`. The Infrastructure project already has the Markdig NuGet package (`Markdig 0.40.0`).

**Template token replacement** mirrors the existing BlogConnector logic:
- `{{title}}` -- `PreprocessedContent.Title`
- `{{content}}` -- Markdig HTML output
- `{{date}}` -- current date formatted as `yyyy-MM-dd`
- `{{author}}` -- from `BlogConnectorOptions.Author`
- `{{tags}}` -- `string.Join(", ", PreprocessedContent.Tags)`
- `{{category}}` -- content type (will need to be passed through or derived; `PreprocessedContent` currently doesn't carry `ContentType`, so either extend it or use a reasonable default)

**Design note on ContentType:** The `PreprocessedContent` record (defined in section 01) does not include `ContentType`. The BlogFormatter needs it for the `{{category}}` template token. Options:
1. Add `ContentType` to `PreprocessedContent` -- cleanest, but requires section 01 coordination.
2. Pass the full `Content` entity to the formatter -- violates the preprocessing abstraction.
3. Default `{{category}}` to empty string and address later.

Recommendation: Add `ContentType` to `PreprocessedContent` in section 01. If section 01 is already implemented without it, extend the record at that time.

**Stub signature:**

```csharp
public sealed class BlogFormatter : IPlatformFormatter
{
    public Platform Platform => Platform.Blog;

    public async Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
    {
        // 1. Read template file from BlogConnectorOptions.TemplatePath
        // 2. Convert content.Body to HTML via Markdig pipeline
        // 3. Replace template tokens with content metadata
        // 4. Return rendered HTML
    }
}
```

---

## Keyed DI Registration

The BlogFormatter should be registered via keyed DI in the Infrastructure `DependencyInjection.cs` (handled in section 15):

```csharp
services.AddKeyedScoped<IPlatformFormatter, BlogFormatter>(Platform.Blog);
services.AddScoped<IContentTransformer, ContentTransformer>();
```

This section does NOT modify `DependencyInjection.cs` -- that is deferred to section 15. For testing, the keyed DI resolution is mocked via `IServiceProvider`.

---

## Frontmatter Stripping Detail

YAML frontmatter is common in markdown files:

```markdown
---
title: My Post
tags: [AI, Tech]
date: 2026-05-26
---

The actual content starts here.
```

The stripping logic:
1. Check if `body.TrimStart()` starts with `---`
2. Find the index of the second `---` (after the first line)
3. Return everything after the second `---` delimiter, trimmed of leading whitespace
4. If no closing `---` is found, return the body unchanged (don't strip partial frontmatter)

This is intentionally simple -- no YAML parsing. The frontmatter content is discarded because `Content` entity already has structured `Title`, `Tags`, etc.

---

## Image Path Resolution Detail

Markdown images use the syntax `![alt text](path/to/image.png)`.

The resolver:
1. Use a regex like `!\[([^\]]*)\]\(([^)]+)\)` to find all image references
2. For each match, check if the path starts with `http://` or `https://`
3. If relative, prepend the base URL: `{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}`
4. Replace the relative path in the body with the absolute URL
5. Collect all images (both original relative and already-absolute) into `IReadOnlyList<ImageReference>`

The `ImageReference` record (from section 01) captures:
- Original path (as written in markdown)
- Resolved absolute URL
- Alt text

---

## Relationship to Other Sections

- **Section 01** defines `IContentTransformer`, `IPlatformFormatter`, `PreprocessedContent`, `ImageReference`. Must be implemented first.
- **Section 04** migrates `BlogConnector` to use `TransformedContent` from the publish request instead of its internal Markdig conversion. After section 04, the Markdig conversion only lives in `BlogFormatter`.
- **Sections 07-10** implement additional formatters (`MediumFormatter`, `LinkedInFormatter`, `TwitterFormatter`, `SubstackFormatter`), each registered with keyed DI by their `Platform` enum value. They follow the same `IPlatformFormatter` contract established here.
- **Section 15** wires `ContentTransformer` and all formatters into the DI container.
