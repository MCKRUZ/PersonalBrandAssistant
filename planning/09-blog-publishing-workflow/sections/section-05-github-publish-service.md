# Section 05: GitHub Publish Service

## Overview

Implements `IGitHubPublishService` -- commits blog HTML to the matthewkruczek-ai repo via GitHub Contents API and verifies deployment with exponential backoff. Also implements the blog publish API endpoints.

**Depends on:** Section 01 (BlogPublishRequest, BlogPublishOptions), Section 04 (IBlogHtmlGenerator)
**Blocks:** Section 07 (Staggered Scheduling), Section 11 (Blog Publish UI)

---

## Tests (Write First)

### GitHubPublishService Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Blog/GitHubPublishServiceTests.cs`

```csharp
// Test: CommitBlogPostAsync creates file via GitHub Contents API with correct path and base64 content
// Test: CommitBlogPostAsync uses correct commit message format
// Test: CommitBlogPostAsync uses configured author name and email
// Test: CommitBlogPostAsync commits to configured branch
// Test: CommitBlogPostAsync stores commit SHA in result
// Test: CommitBlogPostAsync handles file already exists (fetches sha, sends update)
// Test: CommitBlogPostAsync returns error on GitHub API failure (401, 403, 422)
// Test: CommitBlogPostAsync redacts authorization header in logs
```

### BlogDeployVerifier Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Blog/BlogDeployVerifierTests.cs`

```csharp
// Test: VerifyDeploymentAsync returns true on HTTP 200
// Test: VerifyDeploymentAsync retries with exponential backoff (30s, 60s, 120s, 240s)
// Test: VerifyDeploymentAsync returns false after all retries exhausted
// Test: VerifyDeploymentAsync handles network errors during verification
```

### Endpoint Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/BlogPublishEndpointsTests.cs`

```csharp
// Test: POST /api/content/{id}/blog-publish is blocked when SubstackPostUrl is null
// Test: POST /api/content/{id}/blog-publish regenerates HTML with real canonical URL
// Test: POST /api/content/{id}/blog-publish commits to GitHub and starts verification
// Test: POST /api/content/{id}/blog-publish updates ContentPlatformStatus to Published on success
// Test: POST /api/content/{id}/blog-publish updates ContentPlatformStatus to Failed on verification failure
// Test: POST /api/content/{id}/blog-publish stores BlogDeployCommitSha and BlogPostUrl
// Test: GET /api/content/{id}/blog-prep returns HTML preview
// Test: GET /api/content/{id}/blog-status returns current deploy status
```

---

## Implementation Details

### Interface
File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IGitHubPublishService.cs`
```csharp
public interface IGitHubPublishService
{
    Task<GitCommitResult> CommitBlogPostAsync(BlogPublishRequest request, CancellationToken ct);
    Task<bool> VerifyDeploymentAsync(string blogPostUrl, CancellationToken ct);
}
public record GitCommitResult(string CommitSha, string CommitUrl, bool Success, string? Error);
```

### GitHubPublishService
File: `src/PersonalBrandAssistant.Infrastructure/Services/BlogServices/GitHubPublishService.cs`

- **Named HttpClient** `"GitHubApi"` with base `https://api.github.com`, Accept, User-Agent, X-GitHub-Api-Version headers
- **Fine-grained PAT** from `IConfiguration["GitHub:PersonalAccessToken"]`, added per-request (not on default headers)
- **CommitBlogPostAsync flow**: GET contents path to check exists (fetch sha if 200), PUT with base64 content + sha (if update) + commit message + committer
- **AuthHeaderRedactingHandler**: DelegatingHandler to prevent PAT in logs
- **Error handling**: 401/403/422 mapped to descriptive GitCommitResult errors

### BlogDeployVerifier
File: `src/PersonalBrandAssistant.Infrastructure/Services/BlogServices/BlogDeployVerifier.cs`

Exponential backoff: `initialDelay * 2^attempt` (30s, 60s, 120s, 240s). Use `Task.Delay` with CancellationToken. Separate HttpClient for `matthewkruczek.ai` (not GitHub API client). Return false after all retries.

### Endpoints
File: `src/PersonalBrandAssistant.Api/Endpoints/BlogPublishEndpoints.cs`

```
GET  /api/content/{id}/blog-prep    → BlogHtmlResult (preview)
POST /api/content/{id}/blog-publish → Regenerate HTML, commit, verify, update status
GET  /api/content/{id}/blog-status  → { commitSha, blogUrl, status, publishedAt }
```

POST blog-publish: **Gate on SubstackPostUrl** (400 if null). Regenerate HTML with real canonical. Create BlogPublishRequest. Commit. Verify. Update ContentPlatformStatus.

---

## Files
| File | Action |
|------|--------|
| `Application/Common/Interfaces/IGitHubPublishService.cs` | Create |
| `Application/Common/Models/GitCommitResult.cs` | Create |
| `Infrastructure/Services/BlogServices/GitHubPublishService.cs` | Create |
| `Infrastructure/Services/BlogServices/BlogDeployVerifier.cs` | Create |
| `Infrastructure/Services/BlogServices/AuthHeaderRedactingHandler.cs` | Create |
| `Api/Endpoints/BlogPublishEndpoints.cs` | Create |
| `Infrastructure/DependencyInjection.cs` | Modify (register + named HttpClient) |
| `Api/Program.cs` | Modify |
