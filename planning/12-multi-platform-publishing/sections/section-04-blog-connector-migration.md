# Section 4: Blog Connector Migration

## Overview

This section migrates the existing `BlogConnector` from the platform-specific `IBlogConnector` interface to the unified `IPlatformConnector` interface. The blog connector is the first concrete implementation of the new interface, validating the architecture before adding new platform connectors.

The key changes are:
1. Adapt `BlogConnector` to implement `IPlatformConnector` instead of `IBlogConnector`
2. Remove internal Markdig conversion -- the connector receives already-transformed HTML via `request.TransformedContent`
3. Delete the `IBlogConnector` interface entirely
4. Update all tests and DI registrations that reference `IBlogConnector`
5. Update `TestWebApplicationFactory` in the API test project

## Dependencies

- **Section 01 (Interfaces and Types):** `IPlatformConnector`, `PlatformPublishRequest`, `PlatformPublishResult`, `PlatformCapabilities`, `PublishMode` must exist before this section can be implemented.
- **Section 03 (Content Transformation):** `BlogFormatter` must exist -- it takes over the Markdig markdown-to-HTML conversion that currently lives inside `BlogConnector`. The connector will receive pre-transformed HTML in `request.TransformedContent`.

## Tests First

All tests go in `tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs` (replacing the existing file).

### New Tests to Add

```csharp
// File: tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs

// Test: BlogConnector_ImplementsIPlatformConnector
// Verify BlogConnector is assignable to IPlatformConnector.
// Simple type check: Assert.IsAssignableFrom<IPlatformConnector>(connector)

// Test: BlogConnector_PublishAsync_UsesTransformedContentDirectly
// Provide a PlatformPublishRequest with TransformedContent set to pre-rendered HTML.
// Verify the connector writes that HTML directly to disk WITHOUT running Markdig.
// Read the output file and assert its contents match the TransformedContent,
// not a Markdig-converted version of Content.Body.

// Test: BlogConnector_PublishAsync_ReturnsSuccessWithUrl
// Publish with valid content and successful git operations.
// Assert result.Success == true and result.PublishedUrl matches expected URL pattern.
// Return type is now PlatformPublishResult, not string.

// Test: BlogConnector_PublishAsync_ReturnsFailureOnGitError
// Configure process runner mock to return non-zero exit code for git push.
// Assert result.Success == false and result.ErrorMessage contains the git error.
// The connector should NOT throw -- it returns a failure result instead.

// Test: BlogConnector_GetCapabilities_ReturnsCorrectLimits
// Call GetCapabilities() and assert:
//   MaxCharacters = int.MaxValue (no meaningful limit for blog posts)
//   SupportsMarkdown = false (blog receives HTML, not markdown)
//   SupportsHtml = true
//   SupportsImages = true
//   SupportsScheduling = false (scheduling handled by Hangfire, not the connector)
//   SupportsThreads = false

// Test: BlogConnector_ValidateCredentialsAsync_ChecksRepoPathExists
// Set RepoPath to a directory that exists -> returns true.
// Set RepoPath to a nonexistent directory -> returns false.

// Test: BlogConnector_Platform_ReturnsBlog
// Assert connector.Platform == Platform.Blog
```

### Existing Tests to Migrate

The existing `BlogConnectorTests.cs` has 12 tests. Most need signature updates but their intent stays the same:

**Tests that change significantly:**

- `PublishAsync_ConvertsMarkdownToHtml` -- **DELETE**. Markdig conversion is now `BlogFormatter`'s responsibility (section 03). The connector receives pre-rendered HTML.
- `PublishAsync_InjectsHtmlIntoTemplateWithMetadata` -- **DELETE**. Template application is now `BlogFormatter`'s responsibility.
- `PublishAsync_ReturnsConstructedUrl` -- **REWRITE**. Return type changes from `string` to `PlatformPublishResult`. Assert `result.Success == true` and `result.PublishedUrl == "https://matthewkruczek.ai/posts/my-test-post"`.
- `PublishAsync_HandlesGitPushFailure` -- **REWRITE**. Instead of asserting `ThrowsAsync<InvalidOperationException>`, assert `result.Success == false` and `result.ErrorMessage` contains the error. The new connector catches git errors and returns failure results.
- `PublishAsync_HandlesGitAddFailure` -- **REWRITE**. Same pattern as push failure above.

**Tests that need only signature updates (PlatformPublishRequest instead of Content):**

- `PublishAsync_WritesFileToCorrectPath` -- Build a `PlatformPublishRequest` with `TransformedContent` set to pre-rendered HTML. Assert the file appears at the expected path.
- `PublishAsync_RunsGitAddCommitPush` -- Same test logic, just updated method signature.
- `PublishAsync_CommitUsesStdinToAvoidInjection` -- Same test logic.
- `PublishAsync_SetsWorkingDirectory` -- Same test logic.
- `PublishAsync_EmptyTitle_ThrowsArgumentException` -- This may change to returning a failure result instead of throwing.
- `PublishAsync_EmptyBody_ThrowsArgumentException` -- Same consideration.
- `PublishAsync_MissingTemplate_ThrowsInvalidOperation` -- **DELETE**. Template handling moves to `BlogFormatter`.

**Tests that stay unchanged:**

- `GenerateSlug_ProducesExpectedOutput` -- `GenerateSlug` remains a static utility on `BlogConnector`.
- `GenerateSlug_AllSpecialChars_ReturnsEmpty` -- Same.

### Test Helper Updates

The `CreateTestContent` helper stays but a new `CreatePublishRequest` helper is needed:

```csharp
private PlatformPublishRequest CreatePublishRequest(
    string title = "My Test Post",
    string transformedContent = "<h2>Hello</h2><p>This is <strong>bold</strong> text.</p>",
    IReadOnlyList<string>? tags = null,
    PublishMode mode = PublishMode.Publish)
{
    var content = CreateTestContent(title);
    return new PlatformPublishRequest(
        Content: content,
        TransformedContent: transformedContent,
        Tags: tags ?? ["AI", "Engineering"],
        CanonicalUrl: null,
        Mode: mode);
}
```

## Implementation Details

### File: `src/PBA.Infrastructure/Connectors/BlogConnector.cs`

**Current state:** Implements `IBlogConnector`, has `MarkdownPipeline` field, calls `Markdown.ToHtml()` internally, reads template from disk, does token replacement, throws `InvalidOperationException` on git failures, returns `string` URL.

**Target state:** Implements `IPlatformConnector`, no Markdig dependency, no template reading, receives pre-transformed HTML in `request.TransformedContent`, catches exceptions and returns `PlatformPublishResult`, exposes `Platform` property and `GetCapabilities()`/`ValidateCredentialsAsync()` methods.

Changes to make:

1. **Change interface:** `IBlogConnector` -> `IPlatformConnector`
2. **Remove Markdig:** Delete the `MarkdownPipeline` field, the `using Markdig;` import, and the `Markdown.ToHtml()` call
3. **Remove template handling:** Delete the `File.Exists(opts.TemplatePath)` check, `File.ReadAllTextAsync(templatePath)`, and the `template.Replace(...)` chain. The connector receives fully rendered HTML in `request.TransformedContent`.
4. **Change `PublishAsync` signature:** From `Task<string> PublishAsync(Content content, CancellationToken ct)` to `Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)`
5. **Add `Platform` property:** `public Platform Platform => Platform.Blog;`
6. **Add `GetCapabilities()`:** Return a `PlatformCapabilities` with `MaxCharacters = int.MaxValue`, `SupportsHtml = true`, `SupportsMarkdown = false`, `SupportsImages = true`, `SupportsScheduling = false`, `SupportsThreads = false`, `SupportedMediaTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"]`
7. **Add `ValidateCredentialsAsync()`:** Check that `opts.RepoPath` exists as a directory. Return `true`/`false`.
8. **Error handling:** Wrap git operations in try/catch. On failure, return `PlatformPublishResult(Success: false, PublishedUrl: null, PlatformPostId: null, ErrorMessage: ex.Message)` instead of throwing.
9. **Content writing:** Instead of rendering HTML from markdown, write `request.TransformedContent` directly to the output file. The slug is still generated from `request.Content.Title`.
10. **Return type:** On success, return `PlatformPublishResult(Success: true, PublishedUrl: url, PlatformPostId: slug, ErrorMessage: null)`.

The `GenerateSlug` static method stays on `BlogConnector` -- it's needed for file naming and URL construction. The `BlogConnectorOptions` class is unchanged (still needs `RepoPath`, `RemoteName`, `Branch`, `BaseUrl`). The `TemplatePath` and `Author` options can be removed from `BlogConnectorOptions` since template handling moves to `BlogFormatter`, but that cleanup can be deferred if `BlogFormatter` needs to read the same options.

### File: `src/PBA.Application/Common/Interfaces/IBlogConnector.cs`

**DELETE this file entirely.** All consumers should use `IPlatformConnector` resolved via keyed DI with `Platform.Blog`.

### File: `src/PBA.Infrastructure/DependencyInjection.cs`

Change the registration from:

```csharp
services.AddScoped<IBlogConnector, BlogConnector>();
```

To keyed DI:

```csharp
services.AddKeyedScoped<IPlatformConnector, BlogConnector>(Platform.Blog);
```

The `BlogConnectorOptions` configuration binding (`services.Configure<BlogConnectorOptions>(...)`) stays unchanged.

### File: `src/PBA.Infrastructure/Publishing/ContentPublisher.cs`

The `ContentPublisher` currently injects `IBlogConnector` directly. This section removes that dependency. The full `ContentPublisher` refactor (keyed DI resolution, multi-platform flow) is in **Section 05**. For this section, the minimum change is:

- Remove `IBlogConnector blogConnector` from the constructor
- Replace `blogConnector.PublishAsync(content, ...)` with resolving `IPlatformConnector` via keyed DI

The pragmatic approach: have `ContentPublisher` temporarily accept `[FromKeyedServices(Platform.Blog)] IPlatformConnector blogConnector` in its constructor so it compiles and works for blog-only publishing while Section 05 completes the full refactor.

### File: `tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs`

Update mock from `Mock<IBlogConnector>` to `Mock<IPlatformConnector>`. Update `Setup` calls to match the new `PublishAsync(PlatformPublishRequest, CancellationToken)` signature and `PlatformPublishResult` return type.

### File: `tests/PBA.Api.Tests/TestWebApplicationFactory.cs`

Replace the `IBlogConnector` mock with a keyed `IPlatformConnector` mock:

```csharp
// Remove:
var blogConnectorMock = new Mock<IBlogConnector>();
blogConnectorMock.Setup(x => x.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync("https://blog.test/published-post");
services.AddSingleton(blogConnectorMock.Object);

// Replace with:
var blogConnectorMock = new Mock<IPlatformConnector>();
blogConnectorMock.Setup(x => x.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new PlatformPublishResult(true, "https://blog.test/published-post", "published-post", null));
services.AddKeyedSingleton<IPlatformConnector>(Platform.Blog, blogConnectorMock.Object);
```

## File Summary

| File | Action |
|------|--------|
| `src/PBA.Infrastructure/Connectors/BlogConnector.cs` | Modify -- implement `IPlatformConnector`, remove Markdig/template logic |
| `src/PBA.Application/Common/Interfaces/IBlogConnector.cs` | Delete |
| `src/PBA.Infrastructure/DependencyInjection.cs` | Modify -- change to keyed DI registration |
| `src/PBA.Infrastructure/Publishing/ContentPublisher.cs` | Modify -- replace `IBlogConnector` with keyed `IPlatformConnector` |
| `tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs` | Modify -- rewrite tests for new interface |
| `tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs` | Modify -- update mocks for new interface |
| `tests/PBA.Api.Tests/TestWebApplicationFactory.cs` | Modify -- replace `IBlogConnector` mock with keyed `IPlatformConnector` mock |

## Verification

After implementation, run:

```
dotnet build
dotnet test --filter "FullyQualifiedName~BlogConnectorTests"
dotnet test --filter "FullyQualifiedName~ContentPublisherTests"
dotnet test
```

All existing tests must pass (updated for new signatures). No references to `IBlogConnector` should remain anywhere in the codebase. Verify with:

```
grep -r "IBlogConnector" src/ tests/
```

This should return zero matches.

---

## Implementation Notes (Actual)

### Additional file modified (not in original plan)

- `src/PBA.Application/Features/Content/Commands/PublishContent.cs` -- Had stale `IBlogConnector` reference. Migrated to `[FromKeyedServices(Platform.Blog)] IPlatformConnector` with full `PlatformPublishRequest` construction and `result.Success` failure checking.

### Code review fixes applied

1. **HIGH-1: PlatformPostId not persisted** -- Both `PublishContent.cs` and `ContentPublisher.cs` now set `PlatformPostId = result?.PlatformPostId` in the `ContentPlatformPublish` record. Test added: `PublishAsync_PersistsPlatformPostId`.
2. **MED-1: ContentPublisher ignoring failure** -- `ContentPublisher.cs` now checks `result.Success` after `blogConnector.PublishAsync`. On failure: records `PublishStatus.Failed` + `ErrorMessage`, logs warning, returns early (no state machine transition). Test added: `PublishAsync_RecordsFailure_WhenConnectorFails`.

### Test counts

- BlogConnectorTests: 17 tests (12 original migrated/rewritten + 5 new)
- ContentPublisherTests: 7 tests (5 original + 2 from code review)
- Full suite: 455 tests passing (9 migration + 302 application + 96 infrastructure + 48 API)
