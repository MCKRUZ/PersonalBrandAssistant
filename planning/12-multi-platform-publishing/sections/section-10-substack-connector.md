# Section 10: Substack Connector

## Overview

This section implements `SubstackFormatter` and `SubstackConnector` -- the most complex connector in PBA's multi-platform publishing system. Substack uses a reverse-engineered internal API (no official documentation), cookie-based authentication, and requires markdown-to-Tiptap JSON conversion. The connector must be behind a feature flag from day one due to inherent API instability.

Key challenges unique to this connector:
- **Tiptap JSON conversion** -- Substack's editor uses Tiptap, requiring each markdown AST node to be mapped to a specific Tiptap document node structure
- **Cookie-based auth** -- No OAuth flow; uses `substack.sid`, `sid`, and `substack.lli` session cookies from email/password login
- **Image CDN upload** -- Images must be uploaded to Substack's CDN and URLs replaced in the draft
- **Fragile API surface** -- All endpoints are reverse-engineered; any Substack UI update could break the connector

## Dependencies

This section depends on types and services defined in prior sections. These must exist before implementation:

- **Section 01 (Interfaces and Types):** `IPlatformConnector`, `IPlatformFormatter`, `PlatformPublishRequest`, `PlatformPublishResult`, `PlatformCapabilities`, `PreprocessedContent`, `ImageReference`, `PublishMode`, and the `Medium` value added to the `Platform` enum
- **Section 03 (Content Transformation):** `IContentTransformer`, shared preprocessing pipeline (frontmatter stripping, image path resolution), `PreprocessedContent` record
- **Section 05 (Publisher Refactor):** `ContentPublisher` refactored to use keyed DI resolution of `IPlatformConnector` by `Platform` enum
- **Section 06 (Encryption and OAuth):** `ITokenEncryptor` for decrypting/encrypting session cookies from `PlatformCredential.EncryptedCookies`

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
    public string? EncryptedCookies { get; set; }       // Substack: encrypted session cookies
    public string? EncryptedIntegrationToken { get; set; }
    public bool IsActive { get; set; }
    // ... other fields
}
```

## Tests First

All tests go in `tests/PBA.Infrastructure.Tests/Connectors/`. Test naming follows `MethodName_Scenario_ExpectedResult`.

### File: `tests/PBA.Infrastructure.Tests/Connectors/SubstackFormatterTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Connectors;

public class SubstackFormatterTests
{
    // --- Tiptap JSON Conversion ---

    // Test: Format_ConvertsMarkdownToTiptapJson
    //   Given: PreprocessedContent with Body = "Hello world\n\nSecond paragraph"
    //   When: FormatAsync is called
    //   Then: Output is valid JSON with { type: "doc", content: [...] }
    //         containing paragraph nodes with text content

    // Test: Format_Paragraph_MapsToParagraphNode
    //   Given: Body = "This is a paragraph."
    //   Then: Output contains { "type": "paragraph", "content": [{ "type": "text", "text": "This is a paragraph." }] }

    // Test: Format_Heading_MapsToHeadingNodeWithLevel
    //   Given: Body = "## My Heading"
    //   Then: Output contains { "type": "heading", "attrs": { "level": 2 }, "content": [{ "type": "text", "text": "My Heading" }] }
    //   Verify: level 1 through 6 all map correctly

    // Test: Format_BulletList_MapsToBulletListNode
    //   Given: Body = "- Item one\n- Item two\n- Item three"
    //   Then: Output contains { "type": "bulletList", "content": [{ "type": "listItem", ... }, ...] }
    //         Each listItem wraps a paragraph node with the item text

    // Test: Format_Image_MapsToCaptionedImageNode
    //   Given: Body = "![My caption](https://example.com/photo.png)"
    //   Then: Output contains { "type": "captionedImage", "attrs": { "src": "https://example.com/photo.png" } }
    //   Note: The alt text becomes the caption if Substack supports it

    // Test: Format_CodeBlock_MapsToCodeBlockNode
    //   Given: Body = "```python\nprint('hello')\n```"
    //   Then: Output contains { "type": "codeBlock", "content": [{ "type": "text", "text": "print('hello')" }] }

    // Test: Format_BoldText_AddsMarkToTextNode
    //   Given: Body = "This is **bold** text."
    //   Then: The "bold" text node includes marks: [{ "type": "bold" }]
    //   Also test italic: *italic* -> marks: [{ "type": "italic" }]

    // --- Content Manipulation ---

    // Test: Format_InjectsSubscribeWidget_AfterExecutiveSummary
    //   Given: Body with a heading "## Executive Summary" followed by a paragraph,
    //          then "## Next Section"
    //   Then: Output includes a subscribe widget node
    //         (e.g., { "type": "subscribeWidget" } or equivalent Substack node type)
    //         inserted between the executive summary content and the next section
    //   Note: Determine the exact Tiptap node type Substack uses for subscribe CTAs

    // Test: Format_StripsReferencesSection
    //   Given: Body contains "## References\n- [Link 1](url1)\n- [Link 2](url2)" at the end
    //   Then: The references section is NOT present in the Tiptap output

    // Test: Format_StripsAuthorBio
    //   Given: Body contains "## About the Author\nSome bio text." at the end
    //   Then: The author bio section is NOT present in the Tiptap output

    // --- Platform Property ---

    // Test: Format_Platform_ReturnsSubstack
    //   Assert: formatter.Platform == Platform.Substack
}
```

### File: `tests/PBA.Infrastructure.Tests/Connectors/SubstackConnectorTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Connectors;

public class SubstackConnectorTests
{
    // All tests use MockHttpMessageHandler to intercept HTTP calls.
    // Mock IAppDbContext to return PlatformCredential with EncryptedCookies.
    // Mock ITokenEncryptor to return fixed cookie JSON when Decrypt is called.
    // Mock IOptionsMonitor<SubstackOptions> with PublicationSlug and feature flag.

    // --- Publishing: Draft ---

    // Test: PublishAsync_Draft_CreatesDraftOnly
    //   Setup: Mock POST /api/v1/drafts returning 200 with { id: "12345" }
    //   Given: PlatformPublishRequest with Mode = PublishMode.Draft
    //   When: PublishAsync is called
    //   Then: POST /api/v1/drafts is called with Tiptap JSON body
    //         NO call to /prepublish or /publish endpoints
    //         Result.Success == true
    //         Result.PlatformPostId == "12345"

    // --- Publishing: Full Publish ---

    // Test: PublishAsync_Publish_CreatesDraftThenPublishes
    //   Setup: Mock POST /api/v1/drafts returning { id: "12345" }
    //          Mock POST /api/v1/drafts/12345/prepublish returning 200
    //          Mock POST /api/v1/drafts/12345/publish returning 200 with URL
    //   Given: PlatformPublishRequest with Mode = PublishMode.Publish
    //   Then: Three HTTP calls in order: draft creation, prepublish, publish
    //         Publish body includes { "send_email": true, "audience": "everyone" }
    //         Result.Success == true
    //         Result.PublishedUrl is populated

    // --- Image Upload ---

    // Test: PublishAsync_UploadImages_ReplacesUrlsWithCdnUrls
    //   Setup: Request has Images with local/relative URLs
    //          Mock image upload endpoint returning CDN URLs
    //   Then: Draft body contains the CDN URLs, not the original URLs
    //   Note: The exact upload endpoint path needs to be confirmed from
    //         Substack's internal API (likely POST /api/v1/image)

    // --- Tags ---

    // Test: PublishAsync_AddsTags_AfterDraftCreation
    //   Setup: Mock PUT /api/v1/post/{post_id}/tags returning 200
    //   Given: Request with Tags = ["AI", "Engineering"]
    //   Then: PUT /api/v1/post/12345/tags is called with the tags payload
    //         after draft creation

    // --- Authentication Failures ---

    // Test: PublishAsync_ExpiredCookies_ReturnsAuthFailure
    //   Setup: Mock any Substack endpoint returning 401 or 403
    //   Then: Result.Success == false
    //         Result.ErrorMessage indicates cookie expiration
    //         No auto-relogin attempt is made (user must re-login manually)

    // --- Success Response ---

    // Test: PublishAsync_ReturnsPublishedUrl
    //   Setup: Full publish flow mocked successfully
    //   Then: Result.PublishedUrl contains the Substack post URL
    //         (format: https://{publication}.substack.com/p/{slug})

    // --- Credential Validation ---

    // Test: ValidateCredentialsAsync_ValidCookies_ReturnsTrue
    //   Setup: Mock a simple GET endpoint (e.g., user info) returning 200
    //          with valid session cookies
    //   Then: returns true

    // Test: ValidateCredentialsAsync_ExpiredCookies_ReturnsFalse
    //   Setup: Mock returning 401 with the decrypted cookies
    //   Then: returns false

    // --- Capabilities ---

    // Test: GetCapabilities_ReturnsCorrectValues
    //   Then: MaxCharacters == int.MaxValue (no practical limit)
    //         SupportsMarkdown == false (uses Tiptap JSON, not raw markdown)
    //         SupportsHtml == false
    //         SupportsImages == true
    //         SupportsScheduling == false (limited internal API support)
    //         SupportsThreads == false

    // --- Feature Flag ---

    // Test: PublishAsync_FeatureFlagDisabled_ReturnsFailure
    //   Setup: SubstackOptions.Enabled = false
    //   Then: Result.Success == false
    //         Result.ErrorMessage indicates Substack publishing is disabled
    //         No HTTP calls are made
}
```

## Implementation Details

### File: `src/PBA.Infrastructure/Connectors/SubstackOptions.cs`

Options class for Substack connector configuration. Bound from `appsettings.json` section `"Publishing:Substack"`.

```csharp
namespace PBA.Infrastructure.Connectors;

public sealed class SubstackOptions
{
    public const string SectionName = "Publishing:Substack";

    /// Feature flag -- must be true to allow Substack publishing.
    public bool Enabled { get; init; }

    /// The publication slug (e.g., "matthewkruczek" for matthewkruczek.substack.com).
    public string PublicationSlug { get; init; } = string.Empty;

    /// Default audience for email delivery: "everyone", "paid", "founding".
    public string DefaultAudience { get; init; } = "everyone";
}
```

No secrets in this class. Session cookies are stored encrypted in the `PlatformCredential` table and decrypted at publish time via `ITokenEncryptor`.

### File: `src/PBA.Infrastructure/Connectors/SubstackFormatter.cs`

Implements `IPlatformFormatter` for `Platform.Substack`.

**Responsibilities:**

1. **Markdown-to-Tiptap JSON conversion** -- Parse the markdown body into an AST (using Markdig), then walk the AST to produce a Tiptap-compatible JSON document. This is the most complex transformation in the system. The output must be a JSON string with `{ "type": "doc", "content": [...] }` as the root structure.

2. **Node type mappings:**
   - Paragraph -> `{ "type": "paragraph", "content": [{ "type": "text", "text": "..." }] }`
   - Heading (H1-H6) -> `{ "type": "heading", "attrs": { "level": N }, "content": [...] }`
   - Bullet list -> `{ "type": "bulletList", "content": [{ "type": "listItem", "content": [{ "type": "paragraph", ... }] }] }`
   - Ordered list -> `{ "type": "orderedList", "content": [{ "type": "listItem", ... }] }`
   - Image -> `{ "type": "captionedImage", "attrs": { "src": "url" } }` -- Substack uses `captionedImage` not standard `image`
   - Code block -> `{ "type": "codeBlock", "content": [{ "type": "text", "text": "..." }] }`
   - Blockquote -> `{ "type": "blockquote", "content": [...] }`
   - Horizontal rule -> `{ "type": "horizontalRule" }`
   - Links -> text nodes with marks: `[{ "type": "link", "attrs": { "href": "url" } }]`

3. **Inline mark mappings (applied to text nodes):**
   - Bold (`**text**`) -> marks: `[{ "type": "bold" }]`
   - Italic (`*text*`) -> marks: `[{ "type": "italic" }]`
   - Code (`` `text` ``) -> marks: `[{ "type": "code" }]`
   - Links (`[text](url)`) -> marks: `[{ "type": "link", "attrs": { "href": "url" } }]`

4. **Subscribe widget injection** -- After the "Executive Summary" section (detected by heading text matching), insert a subscribe CTA node. The exact Tiptap node type Substack uses for this should be `{ "type": "subscribeWidget" }` or similar. Insert it between the executive summary paragraph(s) and the next heading.

5. **Strip references section** -- Detect and remove any section headed "References", "Works Cited", or similar at the end of the document. Match by heading text (case-insensitive).

6. **Strip author bio** -- Detect and remove any section headed "About the Author", "Author Bio", or similar at the end of the document.

**Implementation approach:** Use Markdig to parse the markdown into a `MarkdownDocument` AST. Walk the AST using a visitor pattern or recursive descent, building a list of `JsonNode` objects (using `System.Text.Json.Nodes`) for each Tiptap node. Serialize the document structure at the end.

The formatter is registered via keyed DI: `services.AddKeyedScoped<IPlatformFormatter, SubstackFormatter>(Platform.Substack)`.

### File: `src/PBA.Infrastructure/Connectors/SubstackConnector.cs`

Implements `IPlatformConnector` for `Platform.Substack`.

**Constructor dependencies:**
- `HttpClient` (injected via named HttpClient factory -- NOT typed client, because the base address varies by publication slug)
- `IAppDbContext` (to load `PlatformCredential`)
- `ITokenEncryptor` (to decrypt/encrypt session cookies)
- `IOptionsMonitor<SubstackOptions>`
- `ILogger<SubstackConnector>`

**Feature flag check:**
At the top of `PublishAsync`, check `_options.CurrentValue.Enabled`. If `false`, return `PlatformPublishResult(false, null, null, "Substack publishing is disabled. Enable it in configuration.")` immediately. No HTTP calls should be made.

**Cookie management:**

Session cookies are stored as a JSON-serialized dictionary in `PlatformCredential.EncryptedCookies`. The connector decrypts this to get a `Dictionary<string, string>` containing keys like `substack.sid`, `sid`, and `substack.lli`. These cookies must be attached to every request.

Since `HttpClient` via `IHttpClientFactory` does not support per-request cookie containers natively, attach cookies via the `Cookie` request header on each `HttpRequestMessage`:

```csharp
var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
request.Headers.Add("Cookie", cookieHeader);
```

**Login flow (called from section-12 API endpoints, not from the connector):**

The connector does NOT perform login. Login is handled by the platform management endpoint (section 12). When the user submits email/password:
1. `POST https://{publication}.substack.com/api/v1/login` with `{ "email": "...", "password": "..." }`
2. Extract `Set-Cookie` headers from the response to get `substack.sid`, `sid`, `substack.lli`
3. Serialize cookies to JSON, encrypt via `ITokenEncryptor`, store in `PlatformCredential.EncryptedCookies`
4. Do NOT persist email/password

When cookies expire, the user must re-login manually. The connector returns an auth failure result, and the UI prompts re-login.

**Publishing flow (`PublishAsync`):**

1. **Feature flag check** -- if disabled, return failure immediately.
2. **Load credentials** -- Load active `PlatformCredential` for `Platform.Substack`. If none found, return failure.
3. **Decrypt cookies** -- via `ITokenEncryptor.Decrypt(credential.EncryptedCookies)`, deserialize to `Dictionary<string, string>`.
4. **Get user info** -- `GET /api/v1/me` to obtain `byline_id`. Log the response at Debug level.
5. **Create draft** -- `POST /api/v1/drafts` with JSON body:
   ```json
   {
     "draft_title": "{content.Title}",
     "draft_subtitle": "",
     "draft_body": {tiptap-json-from-TransformedContent},
     "draft_bylines": [{"id": byline_id}],
     "type": "newsletter"
   }
   ```
   The `draft_body` is the Tiptap JSON produced by `SubstackFormatter`, parsed back into a `JsonNode` (not double-serialized as a string). Log the full request payload at Debug level.
6. **Upload images** -- For each image in `request.Content` (from the `Images` list on `PreprocessedContent`, if passed through), upload to Substack's CDN:
   - `POST /api/v1/image` with multipart form data
   - Extract the CDN URL from the response
   - Update the image `src` in the draft body
   - This step may require a `PUT /api/v1/drafts/{draft_id}` to update the draft with new image URLs
7. **Add tags** -- `PUT /api/v1/post/{post_id}/tags` with the tags payload.
8. **Publish (if mode is Publish):**
   - `POST /api/v1/drafts/{draft_id}/prepublish` -- validates the draft
   - `POST /api/v1/drafts/{draft_id}/publish` with body:
     ```json
     {
       "send_email": true,
       "audience": "everyone"
     }
     ```
     The `audience` value comes from `SubstackOptions.DefaultAudience`.
9. **Return result** -- `PlatformPublishResult(true, publishedUrl, draftId, null)`.

   If mode is `PublishMode.Draft`, stop after step 7 (tags).
   If mode is `PublishMode.Schedule`, treat as Draft (Substack's internal API has limited scheduling support).

**Error handling:**

The connector must distinguish three failure categories:
- **Auth expired** (401/403) -- Return `PlatformPublishResult(false, null, null, "Substack session cookies expired. Please re-login in Settings.")`. No auto-relogin.
- **API changed** (unexpected response shape, 404 on known endpoints) -- Return failure with descriptive message. Log the full response at Warning level.
- **Rate limited** (429) -- Return failure with retry hint.

**Debug logging:**

Log ALL Substack API request/response payloads at `Debug` level. This is critical for diagnosing when the reverse-engineered API changes. Use structured logging:

```csharp
_logger.LogDebug("Substack API {Method} {Url} request: {Payload}", method, url, requestBody);
_logger.LogDebug("Substack API {Method} {Url} response ({StatusCode}): {Body}", method, url, statusCode, responseBody);
```

**Credential validation (`ValidateCredentialsAsync`):**

Decrypt cookies, make a `GET /api/v1/me` call. Return `true` if 200, `false` otherwise. This validates that the session cookies are still active.

**Capabilities (`GetCapabilities`):**

```csharp
public PlatformCapabilities GetCapabilities() => new(
    MaxCharacters: int.MaxValue,
    SupportsMarkdown: false,  // Uses Tiptap JSON, not raw markdown
    SupportsHtml: false,
    SupportsImages: true,
    SupportsScheduling: false,
    SupportsThreads: false,
    SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif", "image/webp"]
);
```

**Platform property:** `public Platform Platform => Platform.Substack;`

### HttpClient Factory Registration

Register a named HttpClient for `SubstackConnector` in the Infrastructure `DependencyInjection.cs` (or in the `AddPublishingDependencies()` method from section-15). Use a named client (not typed) because the base address depends on the publication slug from options:

```csharp
services.AddHttpClient("Substack", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptionsMonitor<SubstackOptions>>().CurrentValue;
    client.BaseAddress = new Uri($"https://{options.PublicationSlug}.substack.com");
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});
```

The connector resolves this client via `IHttpClientFactory.CreateClient("Substack")`.

### Substack API Response Shapes

For reference when implementing response deserialization. These are based on reverse-engineering efforts and may change without notice.

**GET /api/v1/me response:**
```json
{
  "id": 123456,
  "name": "Matt Kruczek",
  "email": "matthewkruczek@yahoo.com",
  "photo_url": "https://substack.com/img/...",
  "bio": "...",
  "byline_id": 789012
}
```

**POST /api/v1/drafts request body:**
```json
{
  "draft_title": "My Post Title",
  "draft_subtitle": "",
  "draft_body": {
    "type": "doc",
    "content": [
      {
        "type": "paragraph",
        "content": [{ "type": "text", "text": "First paragraph..." }]
      }
    ]
  },
  "draft_bylines": [{ "id": 789012 }],
  "type": "newsletter"
}
```

**POST /api/v1/drafts response:**
```json
{
  "id": 12345,
  "slug": "my-post-title",
  "title": "My Post Title",
  "draft_title": "My Post Title",
  "audience": "everyone",
  "type": "newsletter"
}
```

**POST /api/v1/drafts/{id}/publish response:**
```json
{
  "id": 12345,
  "slug": "my-post-title",
  "canonical_url": "https://matthewkruczek.substack.com/p/my-post-title",
  "audience": "everyone"
}
```

**Error responses (various):**
```json
{
  "error": true,
  "message": "Not authorized"
}
```

Define internal record types for deserializing these responses:

```csharp
internal record SubstackUser(int Id, string Name, string Email, int BylineId);
internal record SubstackDraftResponse(int Id, string Slug, string Title);
internal record SubstackPublishResponse(int Id, string Slug, string CanonicalUrl);
internal record SubstackErrorResponse(bool Error, string Message);
```

Use `System.Text.Json` with snake_case property naming via `JsonSerializerOptions` with `JsonNamingPolicy.SnakeCaseLower` (available in .NET 8+). Substack's API uses snake_case keys.

### Tiptap Document Structure Reference

A complete Tiptap document for Substack looks like this. Use this as a reference for building and validating the formatter output:

```json
{
  "type": "doc",
  "content": [
    {
      "type": "heading",
      "attrs": { "level": 2 },
      "content": [{ "type": "text", "text": "Executive Summary" }]
    },
    {
      "type": "paragraph",
      "content": [
        { "type": "text", "text": "This article explores " },
        { "type": "text", "marks": [{ "type": "bold" }], "text": "important topics" },
        { "type": "text", "text": " in AI engineering." }
      ]
    },
    {
      "type": "subscribeWidget"
    },
    {
      "type": "heading",
      "attrs": { "level": 2 },
      "content": [{ "type": "text", "text": "Main Content" }]
    },
    {
      "type": "paragraph",
      "content": [{ "type": "text", "text": "Body text here..." }]
    },
    {
      "type": "captionedImage",
      "attrs": {
        "src": "https://substackcdn.com/image/fetch/...",
        "title": "",
        "alt": "Diagram showing architecture"
      }
    },
    {
      "type": "bulletList",
      "content": [
        {
          "type": "listItem",
          "content": [
            {
              "type": "paragraph",
              "content": [{ "type": "text", "text": "First bullet point" }]
            }
          ]
        },
        {
          "type": "listItem",
          "content": [
            {
              "type": "paragraph",
              "content": [{ "type": "text", "text": "Second bullet point" }]
            }
          ]
        }
      ]
    },
    {
      "type": "codeBlock",
      "content": [
        { "type": "text", "text": "var x = 42;\nConsole.WriteLine(x);" }
      ]
    },
    {
      "type": "horizontalRule"
    }
  ]
}
```

### File Inventory

| File | Action | Layer |
|------|--------|-------|
| `src/PBA.Infrastructure/Configuration/SubstackOptions.cs` | Create | Infrastructure |
| `src/PBA.Infrastructure/Connectors/SubstackFormatter.cs` | Create | Infrastructure |
| `src/PBA.Infrastructure/Connectors/SubstackConnector.cs` | Create | Infrastructure |
| `tests/PBA.Infrastructure.Tests/Connectors/SubstackFormatterTests.cs` | Create | Tests |
| `tests/PBA.Infrastructure.Tests/Connectors/SubstackConnectorTests.cs` | Create | Tests |

**Note:** SubstackOptions placed in `Configuration/` namespace (not `Connectors/`) to match existing pattern (LinkedInOptions, TwitterOptions).

### Testing Approach

**SubstackFormatterTests** -- No HTTP mocking needed. The formatter operates purely on `PreprocessedContent` and returns Tiptap JSON strings. Validate the output by deserializing back to `JsonNode` and asserting the structure (node types, attributes, marks). Consider capturing real Tiptap JSON examples from a Substack editor session to use as expected output fixtures.

**SubstackConnectorTests** -- Use `MockHttpMessageHandler` to intercept HTTP calls. Create a reusable helper:

```csharp
private static HttpClient CreateMockHttpClient(HttpMessageHandler handler, string publicationSlug = "matthewkruczek")
{
    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri($"https://{publicationSlug}.substack.com")
    };
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    return client;
}
```

Mock `IAppDbContext` to return a `PlatformCredential` with encrypted cookies. Mock `ITokenEncryptor` to return a fixed JSON cookie dictionary when `Decrypt` is called. Mock `IOptionsMonitor<SubstackOptions>` with `Enabled = true` and `PublicationSlug = "matthewkruczek"`.

For the feature flag test, set `Enabled = false` and verify no HTTP calls are made and the result is a failure.

Verify request payloads by capturing the `HttpRequestMessage` content in the mock handler and deserializing it for assertions. This is especially important for the Tiptap JSON body in draft creation.

Use `JsonNamingPolicy.SnakeCaseLower` in `JsonSerializerOptions` for all Substack API serialization/deserialization to match their API convention.

### Configuration (appsettings.json)

Add under the `Publishing` section (non-secret values only):

```json
{
  "Publishing": {
    "Substack": {
      "Enabled": false,
      "PublicationSlug": "matthewkruczek",
      "DefaultAudience": "everyone"
    }
  }
}
```

Note: `Enabled` defaults to `false`. This connector is behind a feature flag from day one. The user must explicitly enable it after confirming the reverse-engineered API is working.

Session cookies are stored in the database (`PlatformCredential` table), not in configuration files.

### Keyed DI Registration

The connector and formatter are registered in section-15 (DI Registration), but for reference the registrations are:

```csharp
services.AddKeyedScoped<IPlatformConnector, SubstackConnector>(Platform.Substack);
services.AddKeyedScoped<IPlatformFormatter, SubstackFormatter>(Platform.Substack);
services.Configure<SubstackOptions>(configuration.GetSection(SubstackOptions.SectionName));
```

### Known Risks and Mitigations

1. **API instability** -- Substack's internal API has no versioning or stability guarantees. Any Substack frontend update could change endpoint paths, request/response shapes, or authentication behavior. Mitigation: feature flag (default off), debug logging of all payloads, defensive deserialization with null checks.

2. **Cookie expiration** -- Session cookies expire after an unknown duration (likely days to weeks). The connector cannot re-login automatically because storing plaintext passwords would be a security violation. Mitigation: `ValidateCredentialsAsync` is called before publishing; the UI shows connection status and prompts re-login when cookies expire.

3. **Tiptap schema changes** -- Substack may update their Tiptap editor version, changing the expected node structure. Mitigation: capture real Tiptap examples as test fixtures; compare formatter output against them.

4. **Image CDN upload** -- The image upload endpoint is the least documented part of the API. It may require specific multipart form field names or additional metadata. Mitigation: log all image upload requests/responses at Debug; degrade gracefully by skipping image upload and using original URLs if the CDN upload fails.

### Implementation Checklist

1. [x] Write `SubstackFormatterTests` (12 tests)
2. [x] Write `SubstackConnectorTests` (13 tests)
3. [x] Implement `SubstackOptions` (with `SendEmailOnPublish` added per review)
4. [x] Implement `SubstackFormatter` with Markdig AST walker and Tiptap JSON builder -- pass formatter tests
5. [x] Implement `SubstackConnector` with cookie management, draft creation, tag assignment, and publish flow -- pass connector tests
6. Register named HttpClient for `SubstackConnector` (deferred to section-15)
7. [x] Verify all 25 tests pass

### Deviations from Plan

1. **SubstackOptions in Configuration namespace** — placed in `PBA.Infrastructure.Configuration` to match LinkedInOptions/TwitterOptions pattern, not in `Connectors` as planned
2. **SendEmailOnPublish option added** — code review identified hardcoded `send_email: true` as dangerous (accidental email blasts). Added configurable option defaulting to `true`
3. **Image CDN upload not implemented** — same as Twitter connector: `Content` entity has no `Images` property with binary data. Image upload deferred until data pipeline supports it
4. **Logging levels adjusted** — response bodies logged at Trace (not Debug) to prevent PII exposure during troubleshooting. Status codes remain at Debug
5. **Pattern matching for nullable flow** — used `is not { } draftId` pattern to eliminate nullable gap per review
6. **Custom MockSubstackHandler** — used custom handler class instead of `Moq.Protected()` for cleaner multi-endpoint test setup
7. **25 tests** (not 22 planned) — 12 formatter + 13 connector, including 2 additional tests for draft creation failure and cookie decryption failure per review
