# Section 08: LinkedIn Connector

## Overview

This section implements `LinkedInFormatter` and `LinkedInConnector` -- the LinkedIn platform connector for PBA's multi-platform publishing system. LinkedIn uses the Community Management API v2, which is production-grade, versioned, and Microsoft-backed. Unlike Medium (bearer token), LinkedIn requires OAuth 2.0 three-legged authentication, meaning the connector depends on the OAuth infrastructure from section-06.

The `LinkedInFormatter` converts markdown content to plain text, truncates to 3000 characters, and appends a "Read more" link when truncation occurs. The `LinkedInConnector` handles token validation/refresh, optional image upload (two-step process), and post creation with LinkedIn's versioned REST headers.

## Dependencies

This section depends on types and services defined in prior sections. These must exist before implementation:

- **Section 01 (Interfaces and Types):** `IPlatformConnector`, `IPlatformFormatter`, `PlatformPublishRequest`, `PlatformPublishResult`, `PlatformCapabilities`, `PreprocessedContent`, `ImageReference`, `PublishMode`, and the `Platform` enum
- **Section 03 (Content Transformation):** `IContentTransformer`, shared preprocessing pipeline (frontmatter stripping, image path resolution), `PreprocessedContent` record
- **Section 05 (Publisher Refactor):** `ContentPublisher` refactored to use keyed DI resolution of `IPlatformConnector` by `Platform` enum
- **Section 06 (Encryption and OAuth):** `ITokenEncryptor` for decrypting access/refresh tokens, `IOAuthService` for token refresh flow, `LinkedInOptions` configuration class

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

public interface IOAuthService
{
    Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct);
    Task<PlatformCredential> ExchangeCodeAsync(Platform platform, string code, string state, CancellationToken ct);
    Task<string> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct);
}
```

```csharp
// From section-06 -- Infrastructure layer
public sealed class LinkedInOptions
{
    public const string SectionName = "Publishing:LinkedIn";

    public bool Enabled { get; init; }
    public string ClientId { get; init; }
    public string ClientSecret { get; init; }
    public string RedirectUri { get; init; }
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
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
    public string? Scopes { get; set; }
    public bool IsActive { get; set; }
    // ... other fields
}
```

## Tests First

All tests go in `tests/PBA.Infrastructure.Tests/Connectors/`. Test naming follows `MethodName_Scenario_ExpectedResult`.

### File: `tests/PBA.Infrastructure.Tests/Connectors/LinkedInFormatterTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Connectors;

public class LinkedInFormatterTests
{
    // Test: Format_StripMarkdown_ToPlainText
    //   Given: PreprocessedContent with Body containing markdown:
    //          "## Heading\n\n**Bold text** and *italic text*.\n\n[A link](https://example.com)\n\n- Item 1\n- Item 2"
    //   When: FormatAsync is called
    //   Then: Output is plain text with no markdown syntax:
    //         "Heading\n\nBold text and italic text.\n\nA link\n\n- Item 1\n- Item 2"
    //   Note: Headings become plain lines (strip ## prefix), bold/italic markers removed,
    //         links become bare text (keep link text, drop URL), bullets preserved as-is

    // Test: Format_PreservesLineBreaksAndBullets
    //   Given: Body with multiple paragraphs separated by double newlines and bullet lists
    //   When: FormatAsync is called
    //   Then: Paragraph spacing (double newlines) is preserved
    //         Bullet items ("- " prefix) are preserved
    //         No extra whitespace added or removed between paragraphs

    // Test: Format_TruncatesTo3000Chars_WithEllipsis
    //   Given: Body that after markdown stripping exceeds 3000 characters
    //          and CanonicalUrl = "https://matthewkruczek.ai/posts/long-post"
    //   When: FormatAsync is called
    //   Then: Output is exactly 3000 characters or less (including the "Read more" suffix)
    //         Output ends with "...\n\nRead more: https://matthewkruczek.ai/posts/long-post"
    //         Truncation happens at a word boundary (don't cut mid-word)

    // Test: Format_AddsReadMoreLink_WhenTruncated
    //   Given: Body exceeding 3000 chars, CanonicalUrl = "https://matthewkruczek.ai/posts/test"
    //   When: FormatAsync is called
    //   Then: Output contains "Read more: https://matthewkruczek.ai/posts/test"
    //   Verify: The "Read more" suffix is included within the 3000 char budget, not appended after

    // Test: Format_Under3000Chars_NoTruncation
    //   Given: Body that after stripping is under 3000 characters
    //   When: FormatAsync is called
    //   Then: Full content is returned without ellipsis or "Read more" link
    //         No modification beyond markdown stripping

    // Test: Format_NullCanonicalUrl_TruncatesWithoutReadMore
    //   Given: Body exceeding 3000 chars, CanonicalUrl = null
    //   When: FormatAsync is called
    //   Then: Output is truncated to 3000 chars with "..." but no "Read more" link

    // Test: Format_CodeBlocks_ConvertToPlainText
    //   Given: Body containing fenced code blocks (```csharp\nvar x = 1;\n```)
    //   When: FormatAsync is called
    //   Then: Code block fences are removed, code content is preserved as plain text

    // Test: Format_Images_Stripped
    //   Given: Body containing markdown images "![alt text](url)"
    //   When: FormatAsync is called
    //   Then: Images are completely removed (LinkedIn text posts don't inline images)

    // Test: Format_Platform_ReturnsLinkedIn
    //   Assert: formatter.Platform == Platform.LinkedIn
}
```

### File: `tests/PBA.Infrastructure.Tests/Connectors/LinkedInConnectorTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Connectors;

public class LinkedInConnectorTests
{
    // --- Token Management ---

    // Test: PublishAsync_ExpiredToken_RefreshesBeforePublishing
    //   Setup: PlatformCredential with AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3)
    //          (within the 5-minute refresh window)
    //   Mock: IOAuthService.RefreshTokenAsync returns new token
    //   Mock: POST /rest/posts returning 201
    //   Then: IOAuthService.RefreshTokenAsync was called
    //         Post creation uses the refreshed token

    // Test: PublishAsync_RefreshFails_ReturnsAuthFailure
    //   Setup: PlatformCredential with expired access token
    //   Mock: IOAuthService.RefreshTokenAsync throws or returns failure
    //   Then: Result.Success == false
    //         Result.ErrorMessage contains "authentication" or "token refresh"
    //         No post creation attempted

    // Test: PublishAsync_ValidToken_DoesNotRefresh
    //   Setup: PlatformCredential with AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
    //   Then: IOAuthService.RefreshTokenAsync was NOT called
    //         Post creation proceeds normally

    // --- Text Post Publishing ---

    // Test: PublishAsync_TextPost_CreatesCorrectPayload
    //   Setup: Mock POST /rest/posts returning 201 with x-restli-id header
    //   Given: PlatformPublishRequest with TransformedContent (plain text), no images
    //   Then: POST body is JSON with:
    //         author = "urn:li:person:{personUrn}"
    //         commentary = request.TransformedContent
    //         visibility = "PUBLIC"
    //         distribution.feedDistribution = "MAIN_FEED"
    //         lifecycleState = "PUBLISHED"
    //   And: Request includes headers:
    //         Authorization: Bearer {decrypted_token}
    //         X-Restli-Protocol-Version: 2.0.0
    //         LinkedIn-Version: 202604

    // --- Image Upload ---

    // Test: PublishAsync_WithImage_UploadsImageFirst
    //   Setup: PlatformPublishRequest with Content that has images (PreprocessedContent.Images is non-empty)
    //   Mock: POST /rest/images?action=initializeUpload returns uploadUrl and image URN
    //   Mock: PUT {uploadUrl} returns 201
    //   Mock: POST /rest/posts returns 201
    //   Then: Image initialization was called with owner = person URN
    //         Raw image bytes were PUT to the upload URL
    //         Post creation payload includes the image URN in content.media
    //   Verify: Calls happen in order: initializeUpload -> PUT image -> create post

    // --- Article Sharing ---

    // Test: PublishAsync_WithArticleLink_IncludesContentObject
    //   Given: PlatformPublishRequest with CanonicalUrl set (sharing a blog post)
    //   Then: POST body includes a content object with:
    //         content.article.source = request.CanonicalUrl
    //         content.article.title = content.Title
    //         content.article.description = first ~200 chars of content

    // --- Response Handling ---

    // Test: PublishAsync_ReturnsPostUrnFromResponseHeader
    //   Setup: Mock POST /rest/posts returning 201 with header "x-restli-id: urn:li:share:12345"
    //   Then: Result.Success == true
    //         Result.PlatformPostId == "urn:li:share:12345"
    //         Result.PublishedUrl == constructed URL from the post URN

    // Test: PublishAsync_HttpError_ReturnsFailure
    //   Setup: Mock POST /rest/posts returning 403
    //   Then: Result.Success == false
    //         Result.ErrorMessage includes status code and response body

    // --- Versioned Headers ---

    // Test: PublishAsync_IncludesVersionHeader
    //   When: Any API call is made
    //   Then: Request includes "LinkedIn-Version" header with value "202604"
    //         Request includes "X-Restli-Protocol-Version" header with value "2.0.0"

    // --- Credential Validation ---

    // Test: ValidateCredentialsAsync_ValidToken_ReturnsTrue
    //   Setup: Mock GET /v2/userinfo returning 200
    //   Then: returns true

    // Test: ValidateCredentialsAsync_ExpiredToken_ReturnsFalse
    //   Setup: Mock GET /v2/userinfo returning 401
    //   Then: returns false

    // --- Capabilities ---

    // Test: GetCapabilities_ReturnsCorrectValues
    //   Then: MaxCharacters == 3000
    //         SupportsMarkdown == false
    //         SupportsHtml == false
    //         SupportsImages == true
    //         SupportsScheduling == false
    //         SupportsThreads == false
    //         SupportedMediaTypes includes "image/png", "image/jpeg", "image/gif"
}
```

## Implementation Details

### File: `src/PBA.Infrastructure/Connectors/LinkedInConnector.cs`

Implements `IPlatformConnector` for `Platform.LinkedIn`.

**Constructor dependencies:**
- `HttpClient` (injected via typed HttpClient factory)
- `IAppDbContext` (to load `PlatformCredential`)
- `ITokenEncryptor` (to decrypt access/refresh tokens)
- `IOAuthService` (to refresh expired tokens)
- `IOptionsMonitor<LinkedInOptions>`
- `ILogger<LinkedInConnector>`

**Token validation and refresh (private helper):**

Before every API call, check whether the access token needs refreshing. LinkedIn access tokens last 60 days, refresh tokens last 365 days.

1. Load the active `PlatformCredential` for `Platform.LinkedIn` from the database.
2. Decrypt the access token via `ITokenEncryptor.Decrypt(credential.EncryptedAccessToken)`.
3. If `credential.AccessTokenExpiresAt` is within 5 minutes of now (or already past), call `IOAuthService.RefreshTokenAsync(credential)` to get a new access token. The `IOAuthService` handles the HTTP call to `POST https://www.linkedin.com/oauth/v2/accessToken` with `grant_type=refresh_token` and updates the stored credential.
4. If refresh fails (refresh token also expired, or network error), return a failure `PlatformPublishResult` with an authentication error message. Do not attempt the publish.

**Publishing flow (`PublishAsync`):**

1. Validate/refresh token (as above). If auth fails, return failure immediately.
2. Set request headers for all LinkedIn API calls:
   - `Authorization: Bearer {decrypted_token}`
   - `X-Restli-Protocol-Version: 2.0.0`
   - `LinkedIn-Version: 202604` (this is a monthly-versioned header; update as needed)
3. Determine the person URN. The person URN should be stored on the `PlatformCredential` (obtained during initial OAuth callback via `GET /v2/userinfo`). If not stored, call `GET /v2/userinfo` to retrieve it. The response includes a `sub` field which is the person ID; the URN is `urn:li:person:{sub}`.
4. If content has images (from `PlatformPublishRequest.Content` context or detected in the original content), upload via two-step process:
   - **Initialize:** `POST https://api.linkedin.com/rest/images?action=initializeUpload` with JSON body:
     ```json
     {
       "initializeUploadRequest": {
         "owner": "urn:li:person:{personId}"
       }
     }
     ```
     Response provides `value.uploadUrl` (the URL to PUT the image to) and `value.image` (the image URN to reference in the post).
   - **Upload:** `PUT {uploadUrl}` with raw image bytes and appropriate `Content-Type` header (e.g., `image/png`).
5. Create post: `POST https://api.linkedin.com/rest/posts` with JSON body:

   **Text-only post (no article link, no image):**
   ```json
   {
     "author": "urn:li:person:{personId}",
     "commentary": "{request.TransformedContent}",
     "visibility": "PUBLIC",
     "distribution": {
       "feedDistribution": "MAIN_FEED",
       "targetEntities": [],
       "thirdPartyDistributionChannels": []
     },
     "lifecycleState": "PUBLISHED"
   }
   ```

   **Post with article link (when CanonicalUrl is set):**
   ```json
   {
     "author": "urn:li:person:{personId}",
     "commentary": "{request.TransformedContent}",
     "visibility": "PUBLIC",
     "distribution": {
       "feedDistribution": "MAIN_FEED",
       "targetEntities": [],
       "thirdPartyDistributionChannels": []
     },
     "lifecycleState": "PUBLISHED",
     "content": {
       "article": {
         "source": "{request.CanonicalUrl}",
         "title": "{content.Title}",
         "description": "{first ~200 chars of content body}"
       }
     }
   }
   ```

   **Post with uploaded image:**
   ```json
   {
     "author": "urn:li:person:{personId}",
     "commentary": "{request.TransformedContent}",
     "visibility": "PUBLIC",
     "distribution": {
       "feedDistribution": "MAIN_FEED",
       "targetEntities": [],
       "thirdPartyDistributionChannels": []
     },
     "lifecycleState": "PUBLISHED",
     "content": {
       "media": {
         "id": "{imageUrn}"
       }
     }
   }
   ```

6. LinkedIn does not support draft or scheduled posts via API. If `PublishMode.Draft` or `PublishMode.Schedule` is requested, return a failure: `PlatformPublishResult(false, null, null, "LinkedIn API does not support draft or scheduled posts. Content can only be published immediately.")`. The caller (ContentPublisher) should handle this gracefully -- either skip LinkedIn for draft/schedule operations or publish immediately with a warning.
7. Extract the post identifier from the `x-restli-id` response header (e.g., `urn:li:share:12345`). Construct the published URL from this URN. The URL format is `https://www.linkedin.com/feed/update/{urn}`.
8. Return `PlatformPublishResult(true, publishedUrl, postUrn, null)`.

**Error handling:**
- HTTP 401 -> Return `PlatformPublishResult(false, null, null, "LinkedIn access token is invalid or expired. Please reconnect in Settings.")`
- HTTP 403 -> Return `PlatformPublishResult(false, null, null, "LinkedIn API access denied. Verify your app has w_member_social scope.")` -- this typically means missing permissions
- HTTP 422 -> Return failure with the validation error details from the response body
- HTTP 429 -> Return `PlatformPublishResult(false, null, null, "LinkedIn rate limit exceeded. Retry scheduled.")`
- Other HTTP errors -> Return failure with status code and response body excerpt
- Network errors -> Let the exception propagate (the `ContentPublisher` handles these and schedules retries)

**Credential validation (`ValidateCredentialsAsync`):**

Call `GET /v2/userinfo` with the decrypted access token. Return `true` if 200, `false` otherwise. This endpoint is lightweight and confirms the token is valid without making any posting calls.

**Capabilities (`GetCapabilities`):**

```csharp
public PlatformCapabilities GetCapabilities() => new(
    MaxCharacters: 3000,
    SupportsMarkdown: false,
    SupportsHtml: false,
    SupportsImages: true,
    SupportsScheduling: false,
    SupportsThreads: false,
    SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif"]
);
```

**Platform property:** `public Platform Platform => Platform.LinkedIn;`

### File: `src/PBA.Infrastructure/Connectors/LinkedInFormatter.cs`

Implements `IPlatformFormatter` for `Platform.LinkedIn`.

**Responsibilities:**

1. **Strip markdown to plain text** -- Remove all markdown formatting syntax while preserving the semantic content:
   - Headings: `## Heading` becomes `Heading` (strip `#` prefix and leading whitespace)
   - Bold/italic: `**bold**` becomes `bold`, `*italic*` becomes `italic`
   - Links: `[text](url)` becomes `text` (keep display text, drop URL)
   - Images: `![alt](url)` is completely removed (LinkedIn text posts don't support inline images; images are uploaded separately via the connector)
   - Code blocks: remove fences (` ``` `), preserve code content as plain text
   - Inline code: `` `code` `` becomes `code` (strip backticks)
   - Horizontal rules: `---` removed
   - Blockquotes: `> text` becomes `text` (strip `>` prefix)

2. **Preserve structure** -- Maintain paragraph spacing (double newlines between paragraphs) and bullet list formatting (`- ` prefix). LinkedIn renders line breaks in posts, so the visual structure should survive the markdown stripping.

3. **Truncate to 3000 characters** -- LinkedIn's API enforces a 3000-character limit on post commentary. If the stripped plain text exceeds this limit:
   - Calculate the overhead: `"...\n\nRead more: {canonicalUrl}"` length
   - Truncate the body at a word boundary so that body + overhead <= 3000 chars
   - Append `"...\n\nRead more: {canonicalUrl}"` if `CanonicalUrl` is non-null
   - Append just `"..."` if `CanonicalUrl` is null
   - The total output must not exceed 3000 characters

4. **No truncation suffix for short content** -- If content is under 3000 characters after markdown stripping, return it as-is with no modification beyond the stripping.

**Implementation approach:** Use regex-based markdown stripping rather than pulling in a full markdown parser. The transformations needed are straightforward pattern replacements. Process in order: code blocks first (to avoid stripping markdown syntax inside code), then images, links, bold/italic, headings, blockquotes, horizontal rules.

The formatter is registered via keyed DI: `services.AddKeyedScoped<IPlatformFormatter, LinkedInFormatter>(Platform.LinkedIn)`.

### LinkedIn API Response Shapes

For reference when implementing response deserialization:

**GET /v2/userinfo response:**
```json
{
  "sub": "abc123def456",
  "name": "Matt Kruczek",
  "given_name": "Matt",
  "family_name": "Kruczek",
  "picture": "https://media.licdn.com/...",
  "email": "matthewkruczek@yahoo.com"
}
```

The `sub` field is the person ID used to construct the person URN: `urn:li:person:abc123def456`.

**POST /rest/images?action=initializeUpload response:**
```json
{
  "value": {
    "uploadUrlExpiresAt": 1700000000000,
    "uploadUrl": "https://www.linkedin.com/dms-uploads/...",
    "image": "urn:li:image:abc123"
  }
}
```

**POST /rest/posts response (201 Created):**

The response body may be empty or minimal. The important value is in the response header:
- `x-restli-id: urn:li:share:12345678`

This URN is both the `PlatformPostId` and the basis for the published URL.

**Error response (422, etc.):**
```json
{
  "status": 422,
  "serviceErrorCode": 65600,
  "code": "MISSING_REQUIRED_FIELD",
  "message": "Field author is required"
}
```

Define internal record types for deserializing these responses:

```csharp
internal record LinkedInUserInfo(string Sub, string Name, string? Email);
internal record LinkedInImageUploadResponse(LinkedInImageUploadValue Value);
internal record LinkedInImageUploadValue(string UploadUrl, string Image, long UploadUrlExpiresAt);
internal record LinkedInErrorResponse(int Status, int ServiceErrorCode, string Code, string Message);
```

Use `System.Text.Json` with camelCase property naming for serialization/deserialization.

### HttpClient Factory Registration

Register a typed HttpClient for `LinkedInConnector` in the Infrastructure `DependencyInjection.cs` (or in the `AddPublishingDependencies()` method from section-15):

```csharp
services.AddHttpClient<LinkedInConnector>(client =>
{
    client.BaseAddress = new Uri("https://api.linkedin.com");
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
})
.AddStandardResilienceHandler(); // Polly retry for transient failures
```

Note: The `AddStandardResilienceHandler()` call uses the `Microsoft.Extensions.Http.Resilience` package (part of .NET 8+ ecosystem) to add Polly-based retry policies for transient HTTP failures (5xx, timeouts). This is the recommended approach over manual Polly configuration.

### File Inventory

| File | Action | Layer |
|------|--------|-------|
| `src/PBA.Infrastructure/Connectors/LinkedInFormatter.cs` | Create | Infrastructure |
| `src/PBA.Infrastructure/Connectors/LinkedInConnector.cs` | Create | Infrastructure |
| `tests/PBA.Infrastructure.Tests/Connectors/LinkedInFormatterTests.cs` | Create | Tests |
| `tests/PBA.Infrastructure.Tests/Connectors/LinkedInConnectorTests.cs` | Create | Tests |

Note: `LinkedInOptions` is already defined in section-06 (Encryption and OAuth). No new options class is needed for this section.

### Testing Approach

Both test classes use `MockHttpMessageHandler` to intercept HTTP calls. Follow the same pattern established in section-07 (MediumConnector):

```csharp
private static HttpClient CreateMockHttpClient(HttpMessageHandler handler)
{
    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://api.linkedin.com")
    };
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    return client;
}
```

**LinkedInConnectorTests dependencies to mock:**
- `HttpClient` via `MockHttpMessageHandler` -- mock all LinkedIn API endpoints
- `IAppDbContext` -- return a `PlatformCredential` with known encrypted tokens and a person URN
- `ITokenEncryptor` -- return fixed plaintext tokens when `Decrypt` is called
- `IOAuthService` -- mock `RefreshTokenAsync` for token refresh tests
- `IOptionsMonitor<LinkedInOptions>` -- return a fixed `LinkedInOptions` instance

**LinkedInFormatterTests** need no HTTP mocking -- the formatter operates purely on `PreprocessedContent` strings and returns transformed plain text.

**Key test considerations:**
- Token refresh tests need to verify the connector checks `AccessTokenExpiresAt` against the current time. Use a fixed `DateTimeOffset` or `TimeProvider` abstraction to make these deterministic.
- Image upload tests should verify the two-step flow (initialize -> upload -> post) happens in the correct order by asserting the sequence of HTTP calls made to the mock handler.
- The `x-restli-id` response header extraction test should verify that the connector reads from headers, not the response body.

### Configuration (appsettings.json)

Add under the `Publishing` section (non-secret values only):

```json
{
  "Publishing": {
    "LinkedIn": {
      "Enabled": true,
      "RedirectUri": "https://localhost:5001/api/auth/linkedin/callback"
    }
  }
}
```

Secrets (`ClientId`, `ClientSecret`) go in User Secrets for local development:

```json
{
  "Publishing:LinkedIn:ClientId": "your-linkedin-client-id",
  "Publishing:LinkedIn:ClientSecret": "your-linkedin-client-secret"
}
```

The access/refresh tokens are stored in the database (`PlatformCredential` table), not in configuration files.

### Person URN Storage

The person URN (obtained during the initial OAuth callback) needs to be stored on the `PlatformCredential` entity. Section-02 defines `PlatformCredential` but does not include a field for platform-specific user identifiers. There are two options:

1. **Use `Scopes` field** -- The `PlatformCredential.Scopes` field is a string and could be repurposed, but this is semantically wrong.
2. **Store in a dedicated field or JSON metadata** -- Preferred. The person URN (`urn:li:person:{sub}`) can be stored alongside the credential. If `PlatformCredential` doesn't have a suitable field, store it as part of the OAuth callback flow in section-06 by adding a `PlatformUserId` or `Metadata` field. For this section, assume the person URN is available on the credential (either via a dedicated field or by calling `GET /v2/userinfo` with the stored token).

The connector should call `GET /v2/userinfo` as a fallback if the person URN is not found on the credential, but cache the result for the lifetime of the connector instance to avoid repeated calls.

### Keyed DI Registration

The connector and formatter are registered in section-15 (DI Registration), but for reference the registrations are:

```csharp
services.AddKeyedScoped<IPlatformConnector, LinkedInConnector>(Platform.LinkedIn);
services.AddKeyedScoped<IPlatformFormatter, LinkedInFormatter>(Platform.LinkedIn);
services.Configure<LinkedInOptions>(configuration.GetSection(LinkedInOptions.SectionName));
```

### Implementation Checklist

1. Write `LinkedInFormatterTests` (9 tests)
2. Write `LinkedInConnectorTests` (13 tests)
3. Implement `LinkedInFormatter` with markdown stripping and 3000-char truncation -- pass formatter tests
4. Implement `LinkedInConnector` with internal response models, token refresh, image upload, and post creation -- pass connector tests
5. Register typed HttpClient for `LinkedInConnector` with resilience handler
6. Verify all tests pass with `dotnet test tests/PBA.Infrastructure.Tests/`

## What Was Actually Built

### Deviations from Plan

1. **Image upload deferred**: `SupportsImages` set to `false`. The two-step image upload flow (initializeUpload -> PUT -> create post) was not implemented. Dead `LinkedInImageUploadResponse`/`LinkedInImageUploadValue` records removed. Will implement when image data flows through the system.

2. **IOAuthService.RefreshTokenAsync returns `Result<string>`**: The spec showed `Task<string>` but the actual interface (updated in section-06) returns `Task<Result<string>>`. Implementation correctly uses `result.IsSuccess`/`result.Value`.

3. **Person URN via /v2/userinfo every time**: No `PlatformUserId` field exists on `PlatformCredential`. The connector calls `GET /v2/userinfo` per publish to get the person URN. No caching (connector is scoped, one instance per request).

4. **No AddStandardResilienceHandler**: Package not in project. DI registration deferred to section-15.

5. **ValidateCredentialsAsync routes through token refresh** (code review fix): Near-expired tokens get refreshed during validation, not just during publish.

6. **Draft/Schedule rejection with explicit test coverage** (code review fix): Theory test covers both unsupported modes.

### Actual File Inventory

| File | Lines | Tests |
|------|-------|-------|
| `src/PBA.Infrastructure/Connectors/LinkedInFormatter.cs` | 86 | 9 |
| `src/PBA.Infrastructure/Connectors/LinkedInConnector.cs` | 230 | 13 (11 + 2 theory) |
| `tests/PBA.Infrastructure.Tests/Connectors/LinkedInFormatterTests.cs` | 129 | -- |
| `tests/PBA.Infrastructure.Tests/Connectors/LinkedInConnectorTests.cs` | 390 | -- |

### Test Count: 22 (9 formatter + 13 connector)
