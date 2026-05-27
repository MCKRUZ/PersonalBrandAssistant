# Section 07: Medium Connector

## Overview

This section implements `MediumFormatter` and `MediumConnector` -- the first external platform connector in PBA's multi-platform publishing system. Medium is the simplest connector (bearer token auth, REST API, accepts markdown natively), making it a good first validation of the `IPlatformConnector` architecture established in previous sections.

The Medium REST API v1 is officially unsupported but functional. Existing integration tokens continue to work. There is no OAuth flow -- the user enters their integration token manually in the platform connections UI.

## Dependencies

This section depends on types and services defined in prior sections. These must exist before implementation:

- **Section 01 (Interfaces and Types):** `IPlatformConnector`, `IPlatformFormatter`, `PlatformPublishRequest`, `PlatformPublishResult`, `PlatformCapabilities`, `PreprocessedContent`, `ImageReference`, `PublishMode`, and the `Medium` value added to the `Platform` enum
- **Section 03 (Content Transformation):** `IContentTransformer`, shared preprocessing pipeline (frontmatter stripping, image path resolution), `PreprocessedContent` record
- **Section 05 (Publisher Refactor):** `ContentPublisher` refactored to use keyed DI resolution of `IPlatformConnector` by `Platform` enum
- **Section 06 (Encryption and OAuth):** `ITokenEncryptor` for decrypting the Medium integration token from `PlatformCredential.EncryptedIntegrationToken`

## Key Type Signatures (from dependencies)

These are defined in other sections but included here so this section is self-contained:

```csharp
// From section-01 -- Application layer
public interface IPlatformConnector
{
    Platform Platform { get; }
    Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct);
    Task<bool> ValidateCredentialsAsync(CancellationToken ct);
    PlatformCapabilities GetCapabilities();
}

public interface IPlatformFormatter
{
    Platform Platform { get; }
    Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct);
}

public record PlatformPublishRequest(
    Content Content,
    string TransformedContent,
    IReadOnlyList<string> Tags,
    string? CanonicalUrl,
    PublishMode Mode
);

public record PlatformPublishResult(
    bool Success,
    string? PublishedUrl,
    string? PlatformPostId,
    string? ErrorMessage
);

public record PlatformCapabilities(
    int MaxCharacters,
    bool SupportsMarkdown,
    bool SupportsHtml,
    bool SupportsImages,
    bool SupportsScheduling,
    bool SupportsThreads,
    IReadOnlyList<string> SupportedMediaTypes
);

public record PreprocessedContent(
    string Title,
    string Body,
    string? CanonicalUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ImageReference> Images
);

public enum PublishMode { Draft, Publish, Schedule }
```

```csharp
// From section-06 -- Application layer
public interface ITokenEncryptor
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
```

```csharp
// From section-02 -- Domain layer
public class PlatformCredential
{
    public Guid Id { get; init; }
    public Platform Platform { get; set; }
    public string EncryptedAccessToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public string? EncryptedIntegrationToken { get; set; }
    public bool IsActive { get; set; }
    // ... other fields
}
```

## Tests First

All tests go in `tests/PBA.Infrastructure.Tests/Connectors/`. Test naming follows `MethodName_Scenario_ExpectedResult`.

### File: `tests/PBA.Infrastructure.Tests/Connectors/MediumFormatterTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Connectors;

public class MediumFormatterTests
{
    // Test: Format_InjectsCanonicalUrlFooter
    //   Given: PreprocessedContent with CanonicalUrl = "https://matthewkruczek.ai/posts/my-post"
    //   When: FormatAsync is called
    //   Then: Output ends with a markdown footer containing the canonical URL
    //         e.g., "\n\n---\n*Originally published at [matthewkruczek.ai](https://matthewkruczek.ai/posts/my-post)*"

    // Test: Format_ResolvesRelativeImageUrls_ToAbsolute
    //   Given: Body contains "![alt](images/photo.png)"
    //   When: FormatAsync is called
    //   Then: Output contains "![alt](https://matthewkruczek.ai/images/photo.png)"
    //   Note: Base URL comes from configuration or the canonical URL domain

    // Test: Format_ConvertsSvgReferences_ToPng
    //   Given: Body contains "![diagram](https://example.com/chart.svg)"
    //   When: FormatAsync is called
    //   Then: Output contains "![diagram](https://example.com/chart.png)"

    // Test: Format_PreservesMarkdownFormat
    //   Given: Body with headings, bold, links, code blocks
    //   When: FormatAsync is called
    //   Then: Output retains all markdown syntax unchanged (no HTML conversion)

    // Test: Format_NullCanonicalUrl_OmitsFooter
    //   Given: PreprocessedContent with CanonicalUrl = null
    //   When: FormatAsync is called
    //   Then: No canonical URL footer is appended

    // Test: Format_Platform_ReturnsMedium
    //   Assert: formatter.Platform == Platform.Medium
}
```

### File: `tests/PBA.Infrastructure.Tests/Connectors/MediumConnectorTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Connectors;

public class MediumConnectorTests
{
    // --- Publishing ---

    // Test: PublishAsync_Draft_SendsCorrectPayload
    //   Setup: Mock HTTP returning user ID from /v1/me, 201 from /v1/users/{id}/posts
    //   Given: PlatformPublishRequest with Mode = PublishMode.Draft
    //   When: PublishAsync is called
    //   Then: POST body includes publishStatus = "draft"
    //         POST body includes contentFormat = "markdown"
    //         POST body includes title, content (TransformedContent), tags, canonicalUrl

    // Test: PublishAsync_Public_SendsCorrectPayload
    //   Given: PlatformPublishRequest with Mode = PublishMode.Publish
    //   Then: POST body includes publishStatus = "public"

    // Test: PublishAsync_TruncatesTags_ToMax3
    //   Given: Request with Tags = ["AI", "Engineering", "C#", "Web", "DevOps"]
    //   Then: POST body includes only first 3 tags
    //   Note: Medium API enforces max 3 tags; each max 25 chars -- truncate tag text too

    // Test: PublishAsync_IncludesCanonicalUrl
    //   Given: Request with CanonicalUrl = "https://matthewkruczek.ai/posts/my-post"
    //   Then: POST body includes canonicalUrl field

    // Test: PublishAsync_ReturnsPublishedUrlFromResponse
    //   Setup: Mock response with { data: { id: "abc123", url: "https://medium.com/@user/title-abc123" } }
    //   Then: Result.PublishedUrl == "https://medium.com/@user/title-abc123"
    //         Result.PlatformPostId == "abc123"
    //         Result.Success == true

    // --- Error Handling ---

    // Test: PublishAsync_InvalidToken_ReturnsFailureResult
    //   Setup: Mock /v1/me returning 401
    //   Then: Result.Success == false
    //         Result.ErrorMessage contains "token" or "unauthorized"

    // Test: PublishAsync_RateLimited_ReturnsFailureWithRetryHint
    //   Setup: Mock /v1/users/{id}/posts returning 429
    //   Then: Result.Success == false
    //         Result.ErrorMessage contains "rate limit"

    // --- Credential Validation ---

    // Test: ValidateCredentialsAsync_ValidToken_ReturnsTrue
    //   Setup: Mock /v1/me returning 200 with valid user JSON
    //   Then: returns true

    // Test: ValidateCredentialsAsync_InvalidToken_ReturnsFalse
    //   Setup: Mock /v1/me returning 401
    //   Then: returns false

    // --- Capabilities ---

    // Test: GetCapabilities_ReturnsCorrectValues
    //   Then: MaxCharacters has no practical limit (use int.MaxValue or similar sentinel)
    //         SupportsMarkdown == true
    //         SupportsHtml == true
    //         SupportsImages == true
    //         SupportsScheduling == false
    //         SupportsThreads == false
}
```

## Implementation Details

### File: `src/PBA.Infrastructure/Connectors/MediumOptions.cs`

Options class for Medium connector configuration. Bound from `appsettings.json` section `"Publishing:Medium"`.

```csharp
namespace PBA.Infrastructure.Connectors;

public sealed class MediumOptions
{
    public const string SectionName = "Publishing:Medium";

    public bool Enabled { get; init; }
    public string DefaultPublishStatus { get; init; } = "draft";
}
```

No secrets in this class. The integration token is stored encrypted in the `PlatformCredential` table and decrypted at publish time via `ITokenEncryptor`.

### File: `src/PBA.Infrastructure/Connectors/MediumFormatter.cs`

Implements `IPlatformFormatter` for `Platform.Medium`.

**Responsibilities:**
1. **Canonical URL footer** -- If `PreprocessedContent.CanonicalUrl` is non-null, append a markdown divider and attribution link at the end of the body. Format: `\n\n---\n*Originally published at [matthewkruczek.ai]({canonicalUrl})*`
2. **SVG to PNG conversion** -- Scan for markdown image references (`![alt](url.svg)`) and replace `.svg` extensions with `.png`. Medium does not render SVGs inline.
3. **Absolute image URLs** -- Resolve any relative image paths (e.g., `images/photo.png`) to absolute URLs using the canonical URL domain as the base, or a configured base URL fallback.
4. **Preserve markdown** -- Medium's API accepts markdown natively (`contentFormat: "markdown"`), so no HTML conversion is needed. The formatter must not alter markdown syntax.

The formatter is registered via keyed DI: `services.AddKeyedScoped<IPlatformFormatter, MediumFormatter>(Platform.Medium)`.

### File: `src/PBA.Infrastructure/Connectors/MediumConnector.cs`

Implements `IPlatformConnector` for `Platform.Medium`.

**Constructor dependencies:**
- `HttpClient` (injected via typed HttpClient factory)
- `IAppDbContext` (to load `PlatformCredential`)
- `ITokenEncryptor` (to decrypt the integration token)
- `IOptionsMonitor<MediumOptions>`
- `ILogger<MediumConnector>`

**Publishing flow (`PublishAsync`):**

1. Load the active `PlatformCredential` for `Platform.Medium` from the database.
2. Decrypt the integration token via `ITokenEncryptor.Decrypt(credential.EncryptedIntegrationToken)`.
3. Set `Authorization: Bearer {token}` header on the HttpClient.
4. Call `GET /v1/me` to obtain the authenticated user's `id`. Consider caching this value (e.g., in a private field) after the first successful call per connector instance lifetime, since the user ID doesn't change.
5. Call `POST /v1/users/{userId}/posts` with JSON body:
   ```json
   {
     "title": "{content.Title}",
     "contentFormat": "markdown",
     "content": "{request.TransformedContent}",
     "tags": ["{tag1}", "{tag2}", "{tag3}"],
     "canonicalUrl": "{request.CanonicalUrl}",
     "publishStatus": "draft|public|unlisted"
   }
   ```
6. Map `PublishMode` to Medium's `publishStatus`:
   - `PublishMode.Draft` -> `"draft"`
   - `PublishMode.Publish` -> `"public"`
   - `PublishMode.Schedule` -> `"draft"` (Medium doesn't support scheduling via API; create as draft)
7. Tag handling: Medium allows max 3 tags, each max 25 characters. Take the first 3 tags from the request, truncate each to 25 chars.
8. Parse the response to extract `data.id` and `data.url` for the `PlatformPublishResult`.

**Error handling:**
- HTTP 401 -> Return `PlatformPublishResult(false, null, null, "Medium integration token is invalid or expired. Please reconfigure in Settings.")`
- HTTP 429 -> Return `PlatformPublishResult(false, null, null, "Medium rate limit exceeded. Retry scheduled.")`
- Other HTTP errors -> Return failure with status code and response body excerpt
- Network errors -> Let the exception propagate (the `ContentPublisher` handles these and schedules retries)

**Credential validation (`ValidateCredentialsAsync`):**

Call `GET /v1/me` with the decrypted token. Return `true` if 200, `false` otherwise. This is used by the platform connections UI to show connection status.

**Capabilities (`GetCapabilities`):**

```csharp
public PlatformCapabilities GetCapabilities() => new(
    MaxCharacters: int.MaxValue,
    SupportsMarkdown: true,
    SupportsHtml: true,
    SupportsImages: true,
    SupportsScheduling: false,
    SupportsThreads: false,
    SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif"]
);
```

**Platform property:** `public Platform Platform => Platform.Medium;`

### HttpClient Factory Registration

Register a typed HttpClient for `MediumConnector` in the Infrastructure `DependencyInjection.cs` (or in the `AddPublishingDependencies()` method from section-15):

```csharp
services.AddHttpClient<MediumConnector>(client =>
{
    client.BaseAddress = new Uri("https://api.medium.com");
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Add("Accept-Charset", "utf-8");
});
```

### Medium API Response Shapes

For reference when implementing response deserialization:

**GET /v1/me response:**
```json
{
  "data": {
    "id": "5303d74c64f66366f00cb9b2a94f3251bf",
    "username": "matthewkruczek",
    "name": "Matt Kruczek",
    "url": "https://medium.com/@matthewkruczek"
  }
}
```

**POST /v1/users/{id}/posts response (201 Created):**
```json
{
  "data": {
    "id": "e6f36a",
    "title": "My Post Title",
    "authorId": "5303d74c64f66366f00cb9b2a94f3251bf",
    "url": "https://medium.com/@matthewkruczek/my-post-title-e6f36a",
    "canonicalUrl": "https://matthewkruczek.ai/posts/my-post",
    "publishStatus": "public",
    "license": "",
    "licenseUrl": ""
  }
}
```

**Error response (401, 429, etc.):**
```json
{
  "errors": [
    {
      "message": "Token was invalid.",
      "code": 6003
    }
  ]
}
```

Define internal record types for deserializing these responses:

```csharp
internal record MediumResponse<T>(T Data);
internal record MediumUser(string Id, string Username, string Name, string Url);
internal record MediumPost(string Id, string Title, string Url, string? CanonicalUrl, string PublishStatus);
internal record MediumError(string Message, int Code);
internal record MediumErrorResponse(IReadOnlyList<MediumError> Errors);
```

Use `System.Text.Json` with camelCase property naming (the default in .NET) for serialization/deserialization. The Medium API uses camelCase keys.

### File Inventory

| File | Action | Layer |
|------|--------|-------|
| `src/PBA.Infrastructure/Connectors/MediumOptions.cs` | Create | Infrastructure |
| `src/PBA.Infrastructure/Connectors/MediumFormatter.cs` | Create | Infrastructure |
| `src/PBA.Infrastructure/Connectors/MediumConnector.cs` | Create | Infrastructure |
| `tests/PBA.Infrastructure.Tests/Connectors/MediumFormatterTests.cs` | Create | Tests |
| `tests/PBA.Infrastructure.Tests/Connectors/MediumConnectorTests.cs` | Create | Tests |

### Testing Approach

Both test classes use `MockHttpMessageHandler` to intercept HTTP calls. Create a reusable helper (or use the pattern inline) that builds an `HttpClient` backed by a mocked handler:

```csharp
private static HttpClient CreateMockHttpClient(HttpMessageHandler handler)
{
    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://api.medium.com")
    };
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    return client;
}
```

Mock the `IAppDbContext` to return a `PlatformCredential` with a known encrypted token. Mock `ITokenEncryptor` to return a fixed plaintext token when `Decrypt` is called.

For the `MediumFormatterTests`, no HTTP mocking is needed -- the formatter operates purely on `PreprocessedContent` strings and returns transformed markdown.

### Configuration (appsettings.json)

Add under the `Publishing` section (non-secret values only):

```json
{
  "Publishing": {
    "Medium": {
      "Enabled": true,
      "DefaultPublishStatus": "draft"
    }
  }
}
```

The integration token is stored in the database (`PlatformCredential` table), not in configuration files.

### Keyed DI Registration

The connector and formatter are registered in section-15 (DI Registration), but for reference the registrations are:

```csharp
services.AddKeyedScoped<IPlatformConnector, MediumConnector>(Platform.Medium);
services.AddKeyedScoped<IPlatformFormatter, MediumFormatter>(Platform.Medium);
services.Configure<MediumOptions>(configuration.GetSection(MediumOptions.SectionName));
```

### Implementation Checklist

1. Write `MediumFormatterTests` (6 tests)
2. Write `MediumConnectorTests` (11 tests)
3. Implement `MediumOptions`
4. Implement `MediumFormatter` -- pass formatter tests
5. Implement `MediumConnector` with internal response models -- pass connector tests
6. Register typed HttpClient for `MediumConnector` (deferred to section-15)
7. Verify all tests pass with `dotnet test tests/PBA.Infrastructure.Tests/`

## Deviations from Plan (Code Review Fixes)

1. **Per-request auth headers (CRITICAL-1):** Authorization header set per HttpRequestMessage, not on shared DefaultRequestHeaders. GetUserIdAsync takes token parameter.

2. **Generic error messages (CRITICAL-2):** Catch-all returns "An unexpected error occurred..." instead of raw ex.Message to prevent internal detail leakage.

3. **IOptionsMonitor (HIGH-1):** Changed from IOptions<MediumOptions> to IOptionsMonitor<MediumOptions> for consistency with codebase convention.

4. **DefaultPublishStatus used (HIGH-2):** PublishMode.Schedule and default case read options.CurrentValue.DefaultPublishStatus instead of hardcoded "draft".

5. **Removed userId cache (HIGH-3):** _cachedUserId removed — connector is scoped (one per request), caching provides no value.

6. **Differentiated HTTP errors (HIGH-4):** GetUserIdAsync returns null only for 401 (auth failure). Other non-success statuses throw HttpRequestException for the catch block.

7. **Validate logging (LOW-5):** ValidateCredentialsAsync now logs warnings on failure.

## Test Summary

- **MediumFormatterTests:** 6 tests (canonical footer, null canonical, SVG→PNG, relative images, markdown preservation, platform identity)
- **MediumConnectorTests:** 11 tests (draft payload, public status, tag truncation, published URL, canonical URL, invalid token, rate limit, valid credentials, invalid credentials, capabilities)
- **Total:** 17 new tests, all passing. Full suite: 497 tests green.
