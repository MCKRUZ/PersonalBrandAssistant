# Section 03: Substack RSS Service

## Overview

This section implements the `SubstackService` in the Infrastructure layer -- an RSS feed parser that fetches and parses posts from the configured Substack newsletter URL. The service returns `SubstackPost` records for display in the analytics dashboard's Substack section.

**Depends on:** section-01-backend-models (provides `ISubstackService` interface, `SubstackPost` record, `SubstackOptions` configuration class)

**Blocks:** section-04-dashboard-aggregator, section-06-api-endpoints

---

## File Inventory (Actual)

| Action | File Path |
|--------|-----------|
| Create | `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SubstackService.cs` |
| Create | `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/SubstackServiceTests.cs` |
| Modify | `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` (add DI registration) |

**Deviation from plan:** Test file placed in `Services/Analytics/` (not `Services/AnalyticsServices/`) to match the existing test directory convention used by `GoogleAnalyticsServiceTests.cs`.

---

## Tests FIRST

Create `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/SubstackServiceTests.cs`.

The test class should follow the established project patterns: xUnit + Moq, AAA style, private `CreateSut()` factory method. Use a `MockHttpMessageHandler` (or Moq on `HttpMessageHandler`) to supply canned RSS XML responses to the `HttpClient` injected into the service.

### Test Cases

1. **GetRecentPostsAsync parses valid RSS feed XML into SubstackPost list**
   - Arrange: HttpClient returns a well-formed Atom/RSS feed with 3 items, each containing title, link, published date, and HTML summary.
   - Act: Call `GetRecentPostsAsync(limit: 10, ct)`.
   - Assert: Result is success, list contains 3 `SubstackPost` records, each with correct `Title`, `Url`, `PublishedAt`, and `Summary`.

2. **GetRecentPostsAsync respects limit parameter**
   - Arrange: RSS feed with 5 items.
   - Act: Call with `limit: 2`.
   - Assert: Result contains exactly 2 posts.

3. **GetRecentPostsAsync returns failure when HttpClient throws HttpRequestException**
   - Arrange: `HttpMessageHandler` throws `HttpRequestException`.
   - Act: Call method.
   - Assert: `Result.IsSuccess` is false, `ErrorCode` is `InternalError`.

4. **GetRecentPostsAsync handles malformed RSS XML without crashing (returns failure)**
   - Arrange: HttpClient returns invalid XML (e.g., `"not xml at all"`).
   - Act: Call method.
   - Assert: `Result.IsSuccess` is false.

5. **GetRecentPostsAsync strips HTML from summary field**
   - Arrange: RSS item with summary containing `<p>Hello <b>world</b></p>`.
   - Act: Parse the feed.
   - Assert: `Summary` is `"Hello world"` (no HTML tags, whitespace collapsed).

6. **Posts are ordered by publishedAt descending**
   - Arrange: Feed items with dates Jan 1, Jan 15, Jan 10.
   - Act: Parse the feed.
   - Assert: Posts returned in order Jan 15, Jan 10, Jan 1.

### Test Helper: Sample RSS XML

Include a private helper method or constant that builds a valid Substack-style RSS feed as a string. Substack uses Atom-like feeds. A minimal sample:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>Matt Kruczek's Newsletter</title>
    <link>https://matthewkruczek.substack.com</link>
    <item>
      <title>Post Title Here</title>
      <link>https://matthewkruczek.substack.com/p/post-slug</link>
      <pubDate>Mon, 10 Mar 2026 12:00:00 GMT</pubDate>
      <description><![CDATA[<p>Summary with <b>HTML</b></p>]]></description>
    </item>
  </channel>
</rss>
```

### Test Infrastructure Pattern

The project uses `IHttpClientFactory` for HTTP calls (see the existing `RssFeedPoller` at `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendPollers/RssFeedPoller.cs`). For the `SubstackService`, use a typed `HttpClient` injected via constructor (registered via `AddHttpClient<SubstackService>` or `AddHttpClient("Substack")`).

In tests, create a `DelegatingHandler` subclass (or mock `HttpMessageHandler`) that returns a `StringContent` response with the sample RSS XML. Pass the resulting `HttpClient` directly to the `SubstackService` constructor.

---

## Implementation Details

### SubstackService Class

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SubstackService.cs`

**Namespace:** `PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices`

**Constructor dependencies:**
- `HttpClient` -- typed client for Substack RSS requests
- `IOptions<SubstackOptions>` -- provides `FeedUrl` configuration
- `ILogger<SubstackService>` -- structured logging

**Method:** `GetRecentPostsAsync(int limit, CancellationToken ct)`

Implementation flow:

1. Fetch the RSS feed: `await _httpClient.GetAsync(_options.FeedUrl, ct)`
2. Read the response stream: `await response.Content.ReadAsStreamAsync(ct)`
3. Create an `XmlReader` with `DtdProcessing = DtdProcessing.Ignore` and `Async = true` (same settings as `RssFeedPoller`)
4. Parse via `SyndicationFeed.Load(reader)` -- this handles both RSS 2.0 and Atom formats that Substack may emit
5. Map each `SyndicationItem` to a `SubstackPost` record:
   - `Title` = `item.Title?.Text ?? ""`
   - `Url` = `item.Links.FirstOrDefault()?.Uri?.AbsoluteUri`
   - `PublishedAt` = `item.PublishDate` (fallback to `item.LastUpdatedTime`, fallback to `DateTimeOffset.UtcNow`)
   - `Summary` = HTML-stripped version of `item.Summary?.Text`
6. Order by `PublishedAt` descending
7. Take the requested `limit`
8. Wrap in `Result.Success()`

**Error handling:**
- Catch `HttpRequestException` -- return `Result.Failure(ErrorCode.InternalError, "Failed to fetch Substack RSS feed: {message}")`
- Catch `XmlException` -- return `Result.Failure(ErrorCode.InternalError, "Failed to parse Substack RSS feed: {message}")`
- Catch general `Exception` as a safety net -- log error, return failure
- All error paths should log via `_logger` at Warning or Error level

**HTML stripping:** Reuse the same regex-based approach from `RssFeedPoller`. Use `GeneratedRegex` source generators for the HTML tag regex (`<[^>]+>`) and whitespace collapse regex (`\s{2,}`). Apply `WebUtility.HtmlDecode` after tag removal.

Make the class `internal sealed partial` (partial for source-generated regex) and ensure `InternalsVisibleTo` is configured for the test project (already set in the Infrastructure csproj).

### Key Reference: Existing RssFeedPoller

The existing `RssFeedPoller` at `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendPollers/RssFeedPoller.cs` is a direct reference implementation. It demonstrates:
- How to use `SyndicationFeed.Load()` with `XmlReader` over an HTTP response stream
- HTML stripping with `GeneratedRegex`
- Date extraction from `SyndicationItem` with fallback chain
- Proper `XmlReaderSettings` configuration

The `SubstackService` should follow the same patterns but is simpler because it only needs to extract title, URL, date, and summary (no thumbnail extraction, no trend source mapping).

---

## Configuration

The `SubstackOptions` class is defined in section-01-backend-models. For reference:

```csharp
public class SubstackOptions
{
    public const string SectionName = "Substack";
    public string FeedUrl { get; set; } = "https://matthewkruczek.substack.com/feed";
}
```

The service reads `FeedUrl` from this options object. The default value points to the production Substack newsletter.

---

## Dependency Injection Registration

Add to `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` in the `AddInfrastructure` method:

1. Bind the options: `services.Configure<SubstackOptions>(configuration.GetSection(SubstackOptions.SectionName));`
2. Register typed HttpClient + service:

```csharp
services.AddHttpClient<ISubstackService, SubstackService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "PersonalBrandAssistant/1.0 (+https://github.com/MCKRUZ/personal-brand-assistant)");
});
```

This follows the project's existing convention for named/typed HTTP clients (see the `"RssFeed"` and `"Firecrawl"` registrations in `DependencyInjection.cs`). The 10-second timeout matches the plan's specification for Substack HTTP requests.

---

## Security: SSRF Protection

Per the plan's section 3.8, validate the `SubstackOptions.FeedUrl` hostname to prevent SSRF attacks. The service should check at construction or first use that the URL host matches `*.substack.com`. Implementation approach:

- In the constructor or at the start of `GetRecentPostsAsync`, parse the `FeedUrl` as a `Uri`
- Verify `uri.Host.EndsWith(".substack.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("substack.com", StringComparison.OrdinalIgnoreCase)`
- If validation fails, return `Result.Failure(ErrorCode.ValidationFailed, "Substack feed URL must be a substack.com domain")`
- Log a warning when validation fails

This validation is also covered by a test case in section-05-caching-resilience's security tests, but the implementation belongs here in the service itself.

---

## Docker Configuration

No additional Docker configuration is needed for the Substack service -- it only makes outbound HTTP requests to a public RSS feed. The `HttpClient` timeout (10s) and the resilience policies added in section-05 provide adequate protection.

---

## Summary of Dependencies

| Dependency | Source | Status |
|---|---|---|
| `ISubstackService` interface | section-01-backend-models | Must exist before implementation |
| `SubstackPost` record | section-01-backend-models | Must exist before implementation |
| `SubstackOptions` class | section-01-backend-models | Must exist before implementation |
| `Result<T>` pattern | `Application/Common/Models/Result.cs` | Already exists |
| `ErrorCode` enum | `Application/Common/Errors/ErrorCode.cs` | Already exists |
| `System.ServiceModel.Syndication` NuGet | Infrastructure.csproj | Already referenced |
| Polly resilience policies | section-05-caching-resilience | Added later, not blocking |
| HybridCache wrapping | section-05-caching-resilience | Added later at aggregator/endpoint level |

---

## Implementation Notes (Post-Build)

### Code Review Fixes Applied
- **SSRF hardening:** Added HTTPS scheme validation (`uri.Scheme == Uri.UriSchemeHttps`) to `IsValidSubstackHost()` to reject `http://` and `file://` URLs
- **Cancellation handling:** Added explicit `catch (OperationCanceledException) when (ct.IsCancellationRequested)` to re-throw rather than logging as error
- **XmlResolver:** Set `XmlResolver = null` in XmlReaderSettings for explicit XXE prevention

### Test Summary
- **10 tests**, all passing
- Original 7 from plan + 3 added during review:
  - HTTP scheme SSRF rejection
  - Cancellation token propagation
  - Empty feed handling