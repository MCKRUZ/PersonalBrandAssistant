# Section 03 Code Review Interview

## Auto-fixes Applied

1. **XSS prevention** - Added `WebUtility.HtmlEncode` to all non-Markdig template token replacements (title, author, tags, category) in `BlogFormatter.cs`. Added test `FormatAsync_HtmlEncodesTitle_PreventingXss`.

2. **Date regression** - Added `DateTimeOffset? CreatedAt` to `PreprocessedContent` record. `ContentTransformer` now populates it from `Content.CreatedAt`. `BlogFormatter` uses `content.CreatedAt ?? DateTimeOffset.UtcNow` as fallback. Added test `FormatAsync_UsesCreatedAtDate_NotCurrentDate`.

3. **Frontmatter parsing bug** - Changed `StripFrontmatter` to search for `\n---` (line-start delimiter) instead of `---` anywhere at position 3+. Prevents false matches on `---` inside frontmatter values.

4. **Template-exists guard** - Added `File.Exists` check with `InvalidOperationException` in `BlogFormatter.FormatAsync`, matching the original `BlogConnector` behavior.

## User Decision

5. **BlogConnectorOptions coupling** - User chose "Extract TransformerOptions". Created `TransformerOptions` class with just `BaseUrl`, decoupling `ContentTransformer` from blog-specific config. `BlogFormatter` retains its `BlogConnectorOptions` dependency for `TemplatePath`/`Author`.

## Let Go

- Template caching: premature optimization, no batch publishing yet
- Shared MarkdownPipeline via DI: belongs in section 15 (DI registration)
- Image title regex: unlikely edge case for project content
- String vs enum ContentType: spec chose string for decoupling
- Logging/namespace nitpicks
