# Section 09: Blog Connector

## Overview

This section implements the `IBlogConnector` interface and its `BlogConnector` implementation, which publishes content to a static blog by converting markdown to HTML, injecting it into a template, and pushing via git CLI. This is a standalone infrastructure component with no dependencies on other Content Studio sections.

**Downstream dependency:** Section 05 (PublishContent command) and Section 10 (Hangfire ContentPublisher) both call `IBlogConnector.PublishAsync` when the content's platform is Blog.

---

## Architecture Decision

BlogConnector uses a dedicated typed interface instead of `ConnectorClientBase` (which uses untyped `Dictionary<string, object>` parameters) because git-based file publishing is fundamentally different from HTTP API connectors.

---

## Tests First

All tests go in `tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs`.

### Test Stubs

| Test | Description |
|------|-------------|
| `PublishAsync_ConvertsMarkdownToHtml` | Verify HTML output contains rendered markdown tags |
| `PublishAsync_InjectsHtmlIntoTemplateWithMetadata` | Verify template placeholders replaced with title, date, author, tags |
| `PublishAsync_WritesFileToCorrectPath` | Verify git add targets `posts/{expected-slug}.html` |
| `PublishAsync_RunsGitAddCommitPush` | Verify 3 git commands in order: add, commit, push |
| `PublishAsync_ReturnsConstructedUrl` | Verify return is `{baseUrl}/posts/{slug}` |
| `PublishAsync_GeneratesCorrectSlug` | Title "My Blog Post! (Part 2)" -> "my-blog-post-part-2" |
| `PublishAsync_HandlesGitPushFailure` | Git push fails -> throws InvalidOperationException |
| `PublishAsync_SetsWorkingDirectory` | Git commands use `-C {repoPath}` flag |

---

## Implementation

### 1. Interface: `IBlogConnector`

**File:** `src/PBA.Application/Common/Interfaces/IBlogConnector.cs`

```csharp
public interface IBlogConnector
{
    Task<string> PublishAsync(Content content, CancellationToken ct);
}
```

### 2. Options: `BlogConnectorOptions`

**File:** `src/PBA.Infrastructure/Connectors/BlogConnectorOptions.cs`

Properties: `RepoPath`, `TemplatePath`, `Author` ("Matt Kruczek"), `RemoteName` ("origin"), `Branch` ("main"), `BaseUrl` ("https://matthewkruczek.ai").

### 3. Implementation: `BlogConnector`

**File:** `src/PBA.Infrastructure/Connectors/BlogConnector.cs`

**NuGet dependency:** Add `Markdig` to `PBA.Infrastructure.csproj`.

**Constructor:** `IProcessRunner`, `IOptionsMonitor<BlogConnectorOptions>`, `ILogger<BlogConnector>`

**PublishAsync flow:**
1. Read HTML template from `options.TemplatePath`
2. Convert markdown to HTML: `Markdig.Markdown.ToHtml(content.Body)` with advanced extensions
3. Inject into template -- replace `{{title}}`, `{{content}}`, `{{date}}`, `{{author}}`, `{{tags}}`, `{{category}}`
4. Generate URL slug from title (lowercase, hyphens, strip special chars, collapse consecutive hyphens)
5. Write file to `{repoPath}/posts/{slug}.html`
6. Git operations via IProcessRunner with `-C {repoPath}` flag:
   - `git add posts/{slug}.html`
   - `git commit -m "publish: {title}"`
   - `git push {remoteName} {branch}`
7. On git failure: throw `InvalidOperationException` with stderr
8. Return `{baseUrl}/posts/{slug}`

### 4. DI Registration

**File:** `src/PBA.Infrastructure/DependencyInjection.cs`

```csharp
services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));
services.AddScoped<IBlogConnector, BlogConnector>();
```

### 5. Configuration

**File:** `src/PBA.Api/appsettings.json` -- Add `BlogConnector` section with empty RepoPath/TemplatePath (configured per-environment).

---

## File Summary

| File | Action | Description |
|------|--------|-------------|
| `src/PBA.Application/Common/Interfaces/IBlogConnector.cs` | Create | Interface |
| `src/PBA.Infrastructure/Connectors/BlogConnectorOptions.cs` | Create | Options class |
| `src/PBA.Infrastructure/Connectors/BlogConnector.cs` | Create | Implementation |
| `src/PBA.Infrastructure/PBA.Infrastructure.csproj` | Modify | Add Markdig NuGet |
| `src/PBA.Infrastructure/DependencyInjection.cs` | Modify | Register options + service |
| `src/PBA.Api/appsettings.json` | Modify | Add config section |
| `tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs` | Create | Unit tests |

---

## Key Design Notes

1. **Git CLI over LibGit2Sharp** -- avoids native dependency issues on Docker/Linux
2. **File.WriteAllTextAsync for file writing** -- only git operations go through IProcessRunner
3. **Markdig pipeline:** `new MarkdownPipelineBuilder().UseAdvancedExtensions().Build()`
4. **Error handling:** Git failures throw `InvalidOperationException`. Command handlers wrap in `Result<T>.Fail()`
5. **Template format:** Simple `{{placeholder}}` string replacement, no Razor/Handlebars

---

## Implementation Notes (Post-Build)

### Deviations from Plan
- **Git commit uses `--file=-` with stdin** instead of `-m` flag — prevents command injection from titles containing quotes or shell metacharacters
- **Guard clauses added** — ArgumentException.ThrowIfNullOrWhiteSpace for Title/Body, template existence check, empty slug check
- **Markdig UseAdvancedExtensions()** adds auto-ID attributes to headings (`<h2 id="hello">`) — tests assert heading presence without exact attribute match

### Actual Files Created/Modified
| File | Action |
|------|--------|
| `src/PBA.Application/Common/Interfaces/IBlogConnector.cs` | Created |
| `src/PBA.Infrastructure/Connectors/BlogConnectorOptions.cs` | Created |
| `src/PBA.Infrastructure/Connectors/BlogConnector.cs` | Created |
| `src/PBA.Infrastructure/PBA.Infrastructure.csproj` | Modified (added Markdig 0.40.0) |
| `src/PBA.Infrastructure/DependencyInjection.cs` | Modified (registered BlogConnector) |
| `src/PBA.Api/appsettings.json` | Modified (added BlogConnector config section) |
| `tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs` | Created |

### Test Summary
18 tests total (12 original + 6 edge-case tests from code review):
- 7 Fact tests for happy path
- 1 Theory with 5 InlineData for slug generation
- 5 edge-case tests: empty title, empty body, missing template, git add failure, special-char slug
- 1 command injection prevention test (verifies stdin usage)
