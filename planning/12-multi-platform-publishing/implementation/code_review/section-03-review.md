# Section 03 Code Review: Content Transformation Pipeline

## Verdict: WARNING - merge with fixes

### IMPORTANT

1. **XSS via unsanitized template replacement** - `BlogFormatter` injects `Title`, `Author`, `Tags`, `ContentType` into HTML without encoding. Fix: use `WebUtility.HtmlEncode`.
2. **Date regression** - `BlogFormatter` uses `DateTimeOffset.UtcNow` instead of `content.CreatedAt` (which the original `BlogConnector` uses). `PreprocessedContent` lacks a date field.
3. **Blog-specific options coupling** - `ContentTransformer` depends on `BlogConnectorOptions.BaseUrl` for all platforms. Design smell for a generic class.
4. **Template file re-read on every call** - No caching of template content in `BlogFormatter`.
5. **Frontmatter parsing bug** - `IndexOf("---", 3)` matches `---` inside frontmatter values, not just line-start delimiters.

### SUGGESTION

1. Share `MarkdownPipeline` via DI instead of duplicating in both `BlogConnector` and `BlogFormatter`.
2. Add template-exists guard like the original `BlogConnector`.
3. Image regex doesn't handle `![alt](url "title")` syntax.
4. Missing edge case tests: frontmatter variants, template not found, already-absolute images, XSS payloads.

### NITPICK

1. `PreprocessedContent.ContentType` as `string?` loses enum type safety.
2. Logging after preprocessing but before formatting - no completion/failure logging.
3. Test namespace style differs from production code (non-issue, valid C# convention).
