# Section 09: Twitter Connector

## Overview

This section implements `TwitterFormatter` and `TwitterConnector` -- the Twitter/X platform connector for PBA's multi-platform publishing system. Twitter is the most complex OAuth connector due to PKCE with short-lived tokens (2-hour access token lifetime), chunked media upload, and thread support (reply chaining for content over 280 characters).

The Twitter/X API v2 is used exclusively. Pay-per-use pricing applies (~$0.01/post create). Media upload uses v2 chunked endpoints. If 403 errors occur on v2 media endpoints with OAuth 2.0 in practice, a v1.1 fallback can be added later -- do not build it preemptively.

## Dependencies

This section depends on types and services defined in prior sections. These must exist before implementation:

- **Section 01 (Interfaces and Types):** `IPlatformConnector`, `IPlatformFormatter`, `PlatformPublishRequest`, `PlatformPublishResult`, `PlatformCapabilities`, `PreprocessedContent`, `ImageReference`, `PublishMode`
- **Section 03 (Content Transformation):** `IContentTransformer`, shared preprocessing pipeline (frontmatter stripping, image path resolution), `PreprocessedContent` record
- **Section 05 (Publisher Refactor):** `ContentPublisher` refactored to use keyed DI resolution of `IPlatformConnector` by `Platform` enum
- **Section 06 (Encryption and OAuth):** `ITokenEncryptor` for decrypting stored tokens, `IOAuthService` for PKCE token refresh

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
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

## Tests First

All tests go in `tests/PBA.Infrastructure.Tests/Connectors/`. Test naming follows `MethodName_Scenario_ExpectedResult`.

### File: `tests/PBA.Infrastructure.Tests/Connectors/TwitterFormatterTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Connectors;

public class TwitterFormatterTests
{
    // Test: Format_Under280Chars_ReturnsSingleSegment
    //   Given: PreprocessedContent with Body = "Short tweet about .NET 10"
    //          CanonicalUrl = null
    //   When: FormatAsync is called
    //   Then: Output is the body text as-is (no splitting, no numbering)
    //         Output does not contain thread delimiters or numbering

    // Test: Format_Over280Chars_SplitsIntoThreadSegments
    //   Given: PreprocessedContent with Body = a 600-character plain text paragraph
    //          CanonicalUrl = null
    //   When: FormatAsync is called
    //   Then: Output is a JSON array of string segments (thread parts)
    //         Each segment is <= 280 characters
    //         All segments concatenated reconstruct the full content (minus numbering overhead)

    // Test: Format_SplitsAtSentenceBoundaries
    //   Given: PreprocessedContent with Body = "First sentence here. Second sentence here. Third long sentence..."
    //          (total > 280 chars)
    //   When: FormatAsync is called
    //   Then: Segments split at sentence-ending periods, not mid-word
    //         No segment starts or ends with a partial sentence (when possible)

    // Test: Format_IncludesArticleLinkInFirstOrLastSegment
    //   Given: PreprocessedContent with Body = 600+ chars, CanonicalUrl = "https://matthewkruczek.ai/posts/my-post"
    //   When: FormatAsync is called
    //   Then: Either the first or last segment contains the canonical URL
    //         The URL counts toward the 280-char limit of that segment

    // Test: Format_StripMarkdown_ToPlainText
    //   Given: PreprocessedContent with Body containing **bold**, _italic_, [links](url), ## headings, `code`
    //   When: FormatAsync is called
    //   Then: Output contains no markdown syntax
    //         Bold text preserved without asterisks, italic without underscores
    //         Links become "text (url)" or just "text" depending on length constraints
    //         Headings become plain text

    // Test: Format_PreservesHashtags
    //   Given: PreprocessedContent with Body containing "#dotnet" and "#AI" inline
    //   When: FormatAsync is called
    //   Then: Hashtags are preserved as-is in the output
    //         They are not stripped or modified by the markdown-to-plaintext conversion

    // Test: Format_Platform_ReturnsTwitter
    //   Assert: formatter.Platform == Platform.Twitter
}
```

### File: `tests/PBA.Infrastructure.Tests/Connectors/TwitterConnectorTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Connectors;

public class TwitterConnectorTests
{
    // --- Setup ---
    // Mock HttpMessageHandler for intercepting HTTP calls
    // Mock IAppDbContext to return PlatformCredential with encrypted tokens
    // Mock ITokenEncryptor to return fixed plaintext tokens on Decrypt
    // Mock IOAuthService for token refresh scenarios
    // Create HttpClient with base address https://api.x.com

    // --- Single Tweet Publishing ---

    // Test: PublishAsync_SingleTweet_PostsOnce
    //   Setup: Mock POST /2/tweets returning { data: { id: "12345", text: "Hello" } }
    //   Given: PlatformPublishRequest with TransformedContent = "Hello world" (single segment, no thread)
    //          Mode = PublishMode.Publish
    //   When: PublishAsync is called
    //   Then: Exactly one POST to /2/tweets
    //         Request body contains { "text": "Hello world" }
    //         Authorization header is "Bearer {decrypted-token}"
    //         Result.Success == true
    //         Result.PlatformPostId == "12345"
    //         Result.PublishedUrl contains "12345" (constructed from tweet ID)

    // --- Thread Publishing ---

    // Test: PublishAsync_Thread_ChainsRepliesWithCorrectIds
    //   Setup: Mock POST /2/tweets returning incrementing IDs:
    //          1st call: { data: { id: "100" } }
    //          2nd call: { data: { id: "101" } }
    //          3rd call: { data: { id: "102" } }
    //   Given: TransformedContent is a JSON array with 3 thread segments
    //   When: PublishAsync is called
    //   Then: 3 POST requests to /2/tweets in sequence
    //         1st request has no reply field
    //         2nd request body includes { "reply": { "in_reply_to_tweet_id": "100" } }
    //         3rd request body includes { "reply": { "in_reply_to_tweet_id": "101" } }
    //         Result.PlatformPostId == "100" (first tweet ID is the thread anchor)

    // Test: PublishAsync_ReturnsFirstTweetIdForThreads
    //   Setup: Mock returns IDs "first-tweet", "second-tweet"
    //   Given: TransformedContent is a JSON array with 2 segments
    //   Then: Result.PlatformPostId == "first-tweet"
    //         Result.PublishedUrl references "first-tweet"

    // --- Media Upload ---

    // Test: PublishAsync_WithMedia_UploadsViaChunkedProcess
    //   Setup: Mock the three-step upload:
    //          POST /2/media/upload/initialize -> { media_id: "media-999" }
    //          POST /2/media/upload/media-999/append -> 204 No Content
    //          POST /2/media/upload/media-999/finalize -> { media_id: "media-999" }
    //   Given: PlatformPublishRequest with Content.Images containing one image
    //   When: PublishAsync is called
    //   Then: Three media upload requests in sequence (INIT, APPEND, FINALIZE)
    //         Tweet creation POST includes { "media": { "media_ids": ["media-999"] } }

    // Test: PublishAsync_MediaFinalize_PollsUntilProcessingComplete
    //   Setup: Mock FINALIZE returning { processing_info: { state: "pending", check_after_secs: 1 } }
    //          Then on status check: { processing_info: { state: "succeeded" } }
    //   Given: Request with video/GIF media
    //   When: PublishAsync is called
    //   Then: After FINALIZE, connector polls the status endpoint until state == "succeeded"
    //         Does not proceed to tweet creation until media processing completes

    // --- Token Refresh ---

    // Test: PublishAsync_ExpiredToken_RefreshesBeforePublishing
    //   Setup: PlatformCredential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) (within 10-min threshold)
    //          Mock IOAuthService.RefreshTokenAsync returns new token
    //          Mock POST /2/tweets returning success
    //   When: PublishAsync is called
    //   Then: IOAuthService.RefreshTokenAsync called once before the API call
    //         The refreshed token is used in the Authorization header

    // --- Credential Validation ---

    // Test: ValidateCredentialsAsync_ValidToken_ReturnsTrue
    //   Setup: Mock GET /2/users/me returning 200 with user data
    //   Then: returns true

    // Test: ValidateCredentialsAsync_InvalidToken_ReturnsFalse
    //   Setup: Mock GET /2/users/me returning 401
    //   Then: returns false

    // --- Capabilities ---

    // Test: GetCapabilities_ReturnsCorrectValues
    //   Then: MaxCharacters == 280
    //         SupportsMarkdown == false
    //         SupportsHtml == false
    //         SupportsImages == true
    //         SupportsScheduling == false
    //         SupportsThreads == true
    //         SupportedMediaTypes includes "image/png", "image/jpeg", "image/gif", "video/mp4"
}
```

## Implementation Details

### Thread Segment Format Convention

The `TwitterFormatter` outputs either a plain string (single tweet) or a JSON array of strings (thread). The `TwitterConnector` checks the transformed content:

- If it starts with `[` and parses as a JSON array of strings, treat as a thread -- post each element in sequence using reply chaining.
- Otherwise, treat as a single tweet -- post the content as-is.

This convention allows the connector and formatter to remain decoupled. The connector does not need to know about sentence splitting logic; it just iterates the segments.

### File: `src/PBA.Infrastructure/Connectors/TwitterOptions.cs`

Options class for Twitter connector configuration. Bound from `appsettings.json` section `"Publishing:Twitter"`.

```csharp
namespace PBA.Infrastructure.Connectors;

public sealed class TwitterOptions
{
    public const string SectionName = "Publishing:Twitter";

    public bool Enabled { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;

    /// OAuth 1.0a API key -- only needed if v2 media upload returns 403
    /// and a v1.1 fallback is implemented later. Not used in initial implementation.
    public string? ApiKey { get; init; }

    /// OAuth 1.0a API secret -- same caveat as ApiKey.
    public string? ApiSecret { get; init; }
}
```

`ClientId` and `ClientSecret` are secrets -- they go in User Secrets for dev and environment variables / Key Vault for Docker. Only `Enabled` and `RedirectUri` are safe for `appsettings.json`.

### File: `src/PBA.Infrastructure/Connectors/TwitterFormatter.cs`

Implements `IPlatformFormatter` for `Platform.Twitter`.

**Responsibilities:**

1. **Markdown to plain text** -- Strip all markdown syntax: remove `**`/`__` (bold), `*`/`_` (italic), `#` heading markers, backtick code fences, link syntax (convert `[text](url)` to `text`). Preserve hashtags starting with `#` followed by a word character (distinguish from heading markers which are followed by a space).
2. **Thread splitting** -- If the resulting plain text exceeds 280 characters, split into thread segments:
   - Split at sentence boundaries (period + space, `! `, `? `) near the 280-char limit.
   - Each segment must be <= 280 characters including any numbering.
   - Do not split mid-word. If no sentence boundary is found within the limit, fall back to splitting at the last space before 280 chars.
   - When splitting into a thread, append numbering to each segment: reserve space for ` N/M` suffix (e.g., ` 1/4`). Recalculate after determining total segment count.
3. **Canonical URL insertion** -- If `PreprocessedContent.CanonicalUrl` is non-null, include it in the last segment of a thread (or the single tweet if no thread). The URL counts toward the 280-char limit of that segment. Twitter auto-shortens URLs to 23 characters via t.co, so budget 23 chars for the URL regardless of its actual length.
4. **Output format** -- Return a single plain text string for content <= 280 chars. Return a JSON array of strings (using `System.Text.Json.JsonSerializer.Serialize`) for threads.

**Key implementation notes:**
- URL length: Twitter wraps all URLs in t.co links which are always 23 characters. When calculating whether a segment fits in 280 chars, count any URL as 23 chars, not its actual length.
- Hashtags that already exist in the content body should be preserved. Do not add new hashtags from the `Tags` list -- the v1 codebase did this but it adds complexity around fitting them within the char limit. Tags metadata is passed to the connector but not injected into tweet text by the formatter.

The formatter is registered via keyed DI: `services.AddKeyedScoped<IPlatformFormatter, TwitterFormatter>(Platform.Twitter)`.

### File: `src/PBA.Infrastructure/Connectors/TwitterConnector.cs`

Implements `IPlatformConnector` for `Platform.Twitter`.

**Constructor dependencies:**
- `HttpClient` (injected via typed HttpClient factory, base address `https://api.x.com`)
- `IAppDbContext` (to load `PlatformCredential`)
- `ITokenEncryptor` (to decrypt access and refresh tokens)
- `IOAuthService` (to refresh expired access tokens)
- `IOptionsMonitor<TwitterOptions>`
- `ILogger<TwitterConnector>`

**Token refresh check (before every API call):**

Twitter access tokens expire after 2 hours. Before making any API call:

1. Load the active `PlatformCredential` for `Platform.Twitter`.
2. If `AccessTokenExpiresAt` is within 10 minutes of now, call `IOAuthService.RefreshTokenAsync(credential)`.
3. The `IOAuthService` handles the PKCE token exchange via `POST https://api.twitter.com/2/oauth2/token` with `grant_type=refresh_token`.
4. After refresh, decrypt the updated `EncryptedAccessToken` for use in the Authorization header.
5. If refresh fails (expired refresh token, revoked access), return a failure result with an auth error message.

**Publishing flow (`PublishAsync`):**

1. Refresh token if needed (see above).
2. Decrypt the access token: `ITokenEncryptor.Decrypt(credential.EncryptedAccessToken)`.
3. Set `Authorization: Bearer {token}` header.
4. Parse the `TransformedContent` to determine single tweet vs. thread:
   - Try `JsonSerializer.Deserialize<string[]>(request.TransformedContent)`.
   - If it deserializes to an array with > 1 element, it's a thread.
   - Otherwise, treat the raw `TransformedContent` as a single tweet.
5. If the request has images (`Content` has associated image references), upload media first (see media upload flow below).
6. **Single tweet:** `POST /2/tweets` with `{ "text": "{content}" }` and optional `{ "media": { "media_ids": ["{id}"] } }`.
7. **Thread:** Post each segment in sequence:
   - First segment: `POST /2/tweets` with `{ "text": "{segment[0]}" }`. Capture the returned `data.id`.
   - Each subsequent segment: `POST /2/tweets` with `{ "text": "{segment[n]}", "reply": { "in_reply_to_tweet_id": "{previousId}" } }`. Capture `data.id` for the next reply.
   - The `PlatformPostId` in the result is the first tweet's ID (thread anchor).
8. Construct the `PublishedUrl` as `https://x.com/i/status/{firstTweetId}` (user-agnostic format that Twitter redirects correctly).

**Media upload flow (chunked, v2 endpoints):**

1. **INIT:** `POST /2/media/upload/initialize` with JSON body:
   ```json
   {
     "media_type": "image/png",
     "total_bytes": 123456
   }
   ```
   Response: `{ "media_id": "media-999" }`

2. **APPEND:** `POST /2/media/upload/{media_id}/append` with multipart form data:
   - `media_data`: base64-encoded chunk (max 5MB per chunk)
   - `segment_index`: 0-based chunk index
   For images under 5MB, a single APPEND call suffices.

3. **FINALIZE:** `POST /2/media/upload/{media_id}/finalize`
   Response may include `processing_info` for video/GIF:
   ```json
   {
     "media_id": "media-999",
     "processing_info": {
       "state": "pending",
       "check_after_secs": 5
     }
   }
   ```

4. **STATUS polling (video/GIF only):** If `processing_info.state` is `"pending"` or `"in_progress"`, wait `check_after_secs` seconds then poll `GET /2/media/upload/{media_id}` until `state == "succeeded"` or `"failed"`. Max 10 polls. If `"failed"`, return a publish failure.

**Known risk:** Some developers report 403 errors on v2 media endpoints with OAuth 2.0 tokens. Build using v2 only. If this happens in practice, add a v1.1 fallback later using OAuth 1.0a (the `ApiKey`/`ApiSecret` fields in `TwitterOptions` are reserved for this). Document the risk in a code comment on the media upload method.

**Error handling:**
- HTTP 401 -> Attempt token refresh via `IOAuthService.RefreshTokenAsync`. If refresh succeeds, retry the failed request once. If refresh also fails, return `PlatformPublishResult(false, null, null, "Twitter authentication failed. Please reconnect in Settings.")`.
- HTTP 403 -> Return failure. Log whether this is a media endpoint (potential v2 media limitation) or tweet endpoint (permissions issue).
- HTTP 429 -> Return `PlatformPublishResult(false, null, null, "Twitter rate limit exceeded. Retry scheduled.")`. Extract `x-rate-limit-reset` header if present for retry scheduling.
- Other HTTP errors -> Return failure with status code and response body.

**Credential validation (`ValidateCredentialsAsync`):**

Call `GET /2/users/me` with the decrypted token. Return `true` if 200, `false` otherwise.

**Capabilities (`GetCapabilities`):**

```csharp
public PlatformCapabilities GetCapabilities() => new(
    MaxCharacters: 280,
    SupportsMarkdown: false,
    SupportsHtml: false,
    SupportsImages: true,
    SupportsScheduling: false,
    SupportsThreads: true,
    SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif", "video/mp4"]
);
```

**Platform property:** `public Platform Platform => Platform.Twitter;`

### Twitter API Response Shapes

For reference when implementing response deserialization:

**POST /2/tweets response:**
```json
{
  "data": {
    "id": "1234567890123456789",
    "text": "Hello world"
  }
}
```

**GET /2/users/me response:**
```json
{
  "data": {
    "id": "9876543210",
    "name": "Matt Kruczek",
    "username": "maboroshi_matt"
  }
}
```

**POST /2/media/upload/initialize response:**
```json
{
  "media_id": "1234567890123456789"
}
```

**POST /2/media/upload/{id}/finalize response (image -- immediate):**
```json
{
  "media_id": "1234567890123456789"
}
```

**POST /2/media/upload/{id}/finalize response (video -- async processing):**
```json
{
  "media_id": "1234567890123456789",
  "processing_info": {
    "state": "pending",
    "check_after_secs": 5
  }
}
```

**Error response (401, 403, 429):**
```json
{
  "title": "Unauthorized",
  "type": "about:blank",
  "status": 401,
  "detail": "Unauthorized"
}
```

**Rate limit headers (on every response):**
```
x-rate-limit-limit: 300
x-rate-limit-remaining: 299
x-rate-limit-reset: 1700000000
```

Define internal record types for deserializing these responses:

```csharp
internal record TwitterTweetResponse(TwitterTweetData Data);
internal record TwitterTweetData(string Id, string Text);

internal record TwitterUserResponse(TwitterUserData Data);
internal record TwitterUserData(string Id, string Name, string Username);

internal record TwitterMediaInitResponse(string MediaId);

internal record TwitterMediaFinalizeResponse(
    string MediaId,
    TwitterProcessingInfo? ProcessingInfo
);

internal record TwitterProcessingInfo(
    string State,
    int? CheckAfterSecs
);

internal record TwitterErrorResponse(
    string? Title,
    int? Status,
    string? Detail
);
```

Use `System.Text.Json` with `JsonSerializerDefaults.Web` (camelCase, case-insensitive) for all serialization. Twitter API uses `snake_case` keys, so configure `JsonNamingPolicy.SnakeCaseLower` on the `JsonSerializerOptions`:

```csharp
private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
};
```

### HttpClient Factory Registration

Register a typed HttpClient for `TwitterConnector` in the Infrastructure DI (finalized in section-15):

```csharp
services.AddHttpClient<TwitterConnector>(client =>
{
    client.BaseAddress = new Uri("https://api.x.com");
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});
```

### File Inventory (Actual)

| File | Action | Layer | Notes |
|------|--------|-------|-------|
| `src/PBA.Infrastructure/Configuration/TwitterOptions.cs` | Existed | Infrastructure | Already created in section-06; reused as-is |
| `src/PBA.Infrastructure/Connectors/TwitterFormatter.cs` | Create | Infrastructure | |
| `src/PBA.Infrastructure/Connectors/TwitterConnector.cs` | Create | Infrastructure | |
| `tests/PBA.Infrastructure.Tests/Connectors/TwitterFormatterTests.cs` | Create | Tests | 8 tests |
| `tests/PBA.Infrastructure.Tests/Connectors/TwitterConnectorTests.cs` | Create | Tests | 14 tests |

### Testing Approach

Both test classes use `Moq` to mock `HttpMessageHandler` for intercepting HTTP calls. Create the `HttpClient` backed by the mocked handler:

```csharp
private static HttpClient CreateMockHttpClient(HttpMessageHandler handler)
{
    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://api.x.com")
    };
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    return client;
}
```

**TwitterConnectorTests setup pattern:**
- Mock `IAppDbContext` to return a `PlatformCredential` with known encrypted tokens and an `AccessTokenExpiresAt` far enough in the future to avoid automatic refresh (unless the specific test is testing refresh behavior).
- Mock `ITokenEncryptor.Decrypt()` to return `"test-access-token"`.
- Mock `IOAuthService.RefreshTokenAsync()` to return success with a new token (for refresh tests).
- For thread tests, set up the mock handler to return incrementing tweet IDs on sequential POST calls.
- For media upload tests, set up the mock handler to match different URL patterns (initialize, append, finalize) and return appropriate responses.

**TwitterFormatterTests:** No HTTP mocking needed -- the formatter operates purely on `PreprocessedContent` strings and returns transformed plain text or JSON arrays. Test sentence boundary splitting with carefully constructed inputs that have known sentence breaks at specific character positions.

### Configuration (appsettings.json)

Add under the `Publishing` section (non-secret values only):

```json
{
  "Publishing": {
    "Twitter": {
      "Enabled": false,
      "RedirectUri": "https://localhost:5001/api/auth/twitter/callback"
    }
  }
}
```

`ClientId`, `ClientSecret`, `ApiKey`, and `ApiSecret` go in User Secrets:

```bash
dotnet user-secrets set "Publishing:Twitter:ClientId" "your-client-id"
dotnet user-secrets set "Publishing:Twitter:ClientSecret" "your-client-secret"
```

### Keyed DI Registration

The connector and formatter are registered in section-15 (DI Registration), but for reference:

```csharp
services.AddKeyedScoped<IPlatformConnector, TwitterConnector>(Platform.Twitter);
services.AddKeyedScoped<IPlatformFormatter, TwitterFormatter>(Platform.Twitter);
services.Configure<TwitterOptions>(configuration.GetSection(TwitterOptions.SectionName));
```

### Relationship to V1 Twitter Implementation

The existing codebase has V1 Twitter types in the `PersonalBrandAssistant.Infrastructure` namespace (`TwitterContentFormatter`, `TwitterPlatformAdapter` in `tests/PersonalBrandAssistant.Infrastructure.Tests/`). The V2 implementation in this section lives in the `PBA.Infrastructure.Connectors` namespace and is a clean rewrite using the `IPlatformConnector`/`IPlatformFormatter` architecture. The V1 types should be left as-is for now -- they will be removed in a future cleanup pass after V2 is stable.

### Implementation Checklist (Actual)

1. Write `TwitterFormatterTests` (8 tests) -- DONE
2. Write `TwitterConnectorTests` (14 tests incl. media upload and partial thread failure) -- DONE
3. `TwitterOptions` already existed in `Configuration` namespace -- reused
4. Implement `TwitterFormatter` -- DONE (markdown stripping, sentence-boundary thread splitting, t.co URL budgeting)
5. Implement `TwitterConnector` -- DONE (typed payload records, token refresh with 10-min window, single tweet, thread chaining with partial failure reporting, chunked media upload as internal method)
6. HttpClient factory registration deferred to section-15
7. All 22 tests pass

### Deviations from Plan

- **TwitterOptions**: Already existed in `PBA.Infrastructure.Configuration` from section-06 OAuthService. Not duplicated.
- **Media upload**: Implemented as `internal UploadMediaAsync(mediaType, totalBytes, data, token, ct)` method. Not called from `PublishAsync` because `Content` entity and `PlatformPublishRequest` don't carry image binary data. Ready for integration when the data pipeline is extended.
- **Partial thread failure**: Added per code review -- returns first tweet ID and URL on partial failure so callers can avoid duplicate retries.
- **Enabled guard**: Added `options.CurrentValue.Enabled` check at top of PublishAsync.
- **Typed payloads**: Used `TweetPayload`, `TweetReply`, `TweetMedia` records with `SnakeCaseLower` policy instead of anonymous types.
