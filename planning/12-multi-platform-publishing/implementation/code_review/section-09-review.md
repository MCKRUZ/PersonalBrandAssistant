# Section 09 Review: Twitter Connector

## Critical Issues

No critical issues found.

No hardcoded secrets, no thread-safety problems. Per-request Authorization headers used correctly (same pattern as LinkedIn). Token handling via ITokenEncryptor and IOAuthService follows established patterns.

## High Issues

**[HIGH-1] _options field is assigned but never used -- dead injected dependency**
TwitterConnector.cs line 31: `private readonly IOptionsMonitor<TwitterOptions> _options = options;` is assigned from the primary constructor but never referenced. This is the exact same issue flagged as HIGH-1 in the section-08 LinkedInConnector review and HIGH-2 in the section-07 MediumConnector review. Three connectors now share this pattern.

TwitterOptions contains Enabled, ClientId, ClientSecret, RedirectUri, ApiKey, and ApiSecret -- none are read during publish, validation, or capabilities.

Fix (pick one):
1. Add an enabled guard at the top of PublishAsync:
```csharp
if (!_options.CurrentValue.Enabled)
    return new PlatformPublishResult(false, null, null,
        "Twitter publishing is not enabled. Enable it in Settings.");
```
2. Remove the injection entirely if the enabled check belongs elsewhere (e.g., ContentPublisher pre-filters connectors).

**[HIGH-2] Media upload is specified in the section-09 plan but not implemented**
The section-09 spec explicitly details a chunked media upload flow (INIT/APPEND/FINALIZE/STATUS polling) with corresponding tests PublishAsync_WithMedia_UploadsViaChunkedProcess and PublishAsync_MediaFinalize_PollsUntilProcessingComplete. The implementation has:
- Response record types for TwitterUserResponse, TwitterUserData, TwitterErrorResponse (lines 204-206)
- GetCapabilities() returns SupportsImages: true (line 147)
- No media upload logic anywhere in PublishAsync or any private helper
- No TwitterMediaInitResponse, TwitterMediaFinalizeResponse, or TwitterProcessingInfo records (all specified in the plan)

SupportsImages: true advertises a capability the connector cannot deliver. Downstream code checking capabilities before dispatching will incorrectly believe media upload works.

Fix: Either implement the media upload flow as specified, or set SupportsImages: false and document that media upload is deferred. Remove "video/mp4" from SupportedMediaTypes if no media upload exists.

**[HIGH-3] BuildTweetPayload uses anonymous types with hardcoded snake_case property names, bypassing JsonOptions naming policy**
TwitterConnector.cs lines 194-199: The reply payload is built as:
```csharp
return new { text, reply = new { in_reply_to_tweet_id = inReplyToId } };
```

This works because in_reply_to_tweet_id is literally the C# property name, and System.Text.Json will serialize it as-is. But this is fragile -- it depends on the anonymous type property names matching the API snake_case convention rather than using the configured JsonNamingPolicy.SnakeCaseLower on JsonOptions.

The text property works by accident: SnakeCaseLower converts "text" to "text" (no change). But inReplyToTweetId would become in_reply_to_tweet_id via the policy. The code bypasses the policy by hardcoding the snake_case name directly.

This is not a bug today but creates a maintenance trap. If someone refactors BuildTweetPayload to use PascalCase property names (following C# convention) and relies on JsonOptions to handle casing, the reply field would silently break.

Fix: Use a strongly-typed record with proper C# naming:
```csharp
private record TweetPayload(string Text, TweetReply? Reply = null);
private record TweetReply(string InReplyToTweetId);
```
Then the JsonNamingPolicy.SnakeCaseLower in JsonOptions handles the conversion, and the code is consistent with how the response models work.

**[HIGH-4] Token refresh window inconsistency with LinkedInConnector: 10 minutes vs 5 minutes**
TwitterConnector.cs line 32: TokenRefreshWindow = TimeSpan.FromMinutes(10) while LinkedInConnector.cs line 28: TokenRefreshWindow = TimeSpan.FromMinutes(5).

The spec says 10 minutes for Twitter (justified by the 2-hour token lifetime), while LinkedIn uses 5 minutes (60-day token lifetime). The different values are defensible on their own, but this is a consistency question.

The concern is less about the value and more about discoverability. If someone changes one connector refresh window, they may not realize the other is different.

Fix (low effort): Add a brief comment explaining the choice:
```csharp
// Twitter access tokens expire after 2 hours; refresh early to avoid mid-thread failures
private static readonly TimeSpan TokenRefreshWindow = TimeSpan.FromMinutes(10);
```

Downgrading to MEDIUM if the team considers this intentional and documented. Flagged as HIGH because a future maintainer may assume all connectors share the same window and "fix" one to match the other.

**[HIGH-5] Partial thread failure leaves orphaned tweets with no rollback or reporting**
TwitterConnector.cs lines 58-106: If a 3-segment thread posts segments 1 and 2 successfully but segment 3 fails (rate limit, network error, etc.), the method returns a failure result with no indication that tweets 1 and 2 were already published. The caller sees Success = false and may retry the entire thread, creating duplicate tweets for the first segments.

This is an inherent limitation of Twitter API (no transactional thread creation), but the connector should at least report the partial state.

Fix: On partial thread failure, include the partial state in the error message and return the first tweet ID so the caller can decide whether to retry or clean up:
```csharp
if (!response.IsSuccessStatusCode)
{
    var errorBody = await response.Content.ReadAsStringAsync(ct);
    logger.LogError("Twitter thread failed at segment {Index}/{Total}: {Status} {Body}",
        segmentIndex + 1, segments.Count, response.StatusCode, errorBody);

    if (firstTweetId is not null)
    {
        return new PlatformPublishResult(false,
            $"https://x.com/i/status/{firstTweetId}", firstTweetId,
            $"Thread partially published ({segmentIndex}/{segments.Count} tweets).");
    }
    return new PlatformPublishResult(false, null, null,
        $"Twitter publish failed ({response.StatusCode})");
}
```

## Medium Issues

**[MEDIUM-1] ParseSegments silently swallows JSON deserialization of non-thread content starting with [**
TwitterConnector.cs lines 176-192: If TransformedContent starts with [ but is not valid JSON (e.g., a tweet starting with "[Update]"), the catch (JsonException) block silently falls through and treats the entire content as a single tweet. This is correct behavior, but the empty catch body violates the project convention against swallowing errors silently.

Fix: Add a comment in the catch body:
```csharp
catch (JsonException)
{
    // Content starts with '[' but is not a JSON thread array -- treat as single tweet
}
```

**[MEDIUM-2] ParseSegments returns single-element JSON arrays as raw JSON string**
TwitterConnector.cs line 183: `if (segments is { Length: > 1 })` means a JSON array with exactly one element is ignored and the raw JSON string is posted as-is, which would be a malformed tweet containing JSON brackets.

The formatter currently never produces a single-element JSON array (it only serializes to JSON when there are multiple segments), so this does not trigger today. But it is a defensive coding gap.

Fix: Change to Length > 0:
```csharp
if (segments is { Length: > 0 })
    return [.. segments];
```

**[MEDIUM-3] FindSentenceBoundary only checks for sentence-ending punctuation followed by a space -- misses end-of-string**
TwitterFormatter.cs lines 315-336: The sentence boundary detection requires `searchRegion[i + 1] == ' '` after the punctuation mark. If the text ends with a period at exactly the character limit, the boundary is missed because there is no following space character.

Fix: Also check for sentence-ending at the last character of the search region:
```csharp
if (IsSentenceEnd(searchRegion[i]) &&
    (i == searchRegion.Length - 1 || searchRegion[i + 1] == ' '))
```

**[MEDIUM-4] Thread segment numbering can exceed 280 characters in edge cases**
TwitterFormatter.cs lines 268-293: The estimated segment count is calculated before splitting, then the actual numbering suffix is applied after splitting. If the estimate is wrong (e.g., estimated 9 segments but actually produces 10), the suffix changes from " 9/9" (4 chars) to " 10/10" (6 chars), potentially pushing a segment over 280.

The code at line 291-292 checks the length before appending, which means some segments might silently lack numbering. Acceptable but worth documenting.

**[MEDIUM-5] Format_SingleTweetWithUrl_BudgetsTcoLength test does not verify the 280-char boundary**
TwitterFormatterTests.cs lines 876-887: The test uses 250 chars of text + a URL. Since 250 + 23 (t.co) + 1 (newline) = 274 <= 280, this always fits in a single tweet. The test asserts the URL is present and no thread splitting occurred, but does not verify the boundary case where text + t.co would exceed 280.

Fix: Add a boundary test where text length + 24 (t.co + newline) = exactly 281, triggering thread splitting:
```csharp
[Fact]
public async Task Format_TextPlusUrlExceeds280_SplitsIntoThread()
{
    var textPart = new string('A', 257); // 257 + 23 (t.co) + 1 (newline) = 281 > 280
    var url = "https://matthewkruczek.ai/posts/test";
    var content = CreateContent(textPart, url);

    var result = await _formatter.FormatAsync(content, CancellationToken.None);

    Assert.StartsWith("[", result); // Should be a JSON array (thread)
}
```

**[MEDIUM-6] Missing test for ValidateCredentialsAsync with near-expired token**
The LinkedInConnector review flagged this as HIGH-3: ValidateCredentialsAsync calls GetValidTokenAsync but there is no test verifying that validation triggers refresh for near-expired tokens. The PublishAsync_ExpiredToken_RefreshesBeforePublishing test covers publish but not validate.

Fix: Add a test that seeds a near-expired credential and verifies RefreshTokenAsync is called during ValidateCredentialsAsync.

**[MEDIUM-7] No test verifies the POST /2/tweets request URL path**
All HTTP mock setups use ItExpr.IsAny<HttpRequestMessage>() without verifying the request URL. A typo in the endpoint path (e.g., /2/tweet instead of /2/tweets) would pass all tests.

Fix: At least one test should capture and verify the request URI:
```csharp
Assert.Equal("/2/tweets", capturedRequest.RequestUri?.AbsolutePath);
```

## Low Issues

**[LOW-1] TwitterUserResponse, TwitterUserData, and TwitterErrorResponse records are defined but never used**
TwitterConnector.cs lines 204-206: These internal records exist for deserialization of /2/users/me responses and error responses, but ValidateCredentialsAsync only checks the HTTP status code (line 130). The response body is never deserialized.

Not harmful but dead code. Consider removing or adding a log that includes the username on successful validation.

**[LOW-2] Italic regex in TwitterFormatter uses lookaheads/lookbehinds while LinkedInFormatter uses simple pattern**
TwitterFormatter.cs line 354 uses a more defensive pattern than LinkedInFormatter.cs line 69. Both produce same result given current processing order (bold stripped first). Inconsistent but harmless.

**[LOW-3] TwitterOptions.cs uses required modifier while the spec shows = string.Empty defaults**
The actual implementation uses `required string ClientId` which is better (fails at config binding time rather than silently using empty strings). Improvement over spec.

**[LOW-4] Markdown stripping does not handle ordered/unordered list markers**
No regex for stripping "- " or "1. " list markers. List items would appear with their markers in the tweet. Acceptable for v1.

## Nitpicks

**[NITPICK-1]** JsonOptions naming uses PascalCase matching codebase convention across all connectors. Spec recommended s_jsonOptions. Codebase is consistent; no change needed.

**[NITPICK-2]** Test CreateContent sets PrimaryPlatform = Platform.Twitter. Good -- consistent with LinkedIn tests.

**[NITPICK-3]** URL format `https://x.com/i/status/{firstTweetId}` is correct. The /i/status/ path is user-agnostic and redirects to the correct user context.

## Approved Items

- Per-request Authorization headers on all HTTP calls (lines 70, 128) -- no DefaultRequestHeaders mutation
- Generic error messages to callers with detailed logger.LogError internally (lines 84-85, 91-94, 113-115)
- IOptionsMonitor<TwitterOptions> used (not IOptions<T>) -- consistent with codebase convention
- Differentiated HTTP status handling: 401 (auth), 403 (permissions), 429 (rate limit), generic fallback -- matches LinkedIn pattern
- sealed on both classes -- correct for concrete implementations
- static readonly JsonSerializerOptions with SnakeCaseLower and WhenWritingNull -- avoids per-call allocation, correct for Twitter API
- GeneratedRegex source-generated regex on all 10 formatter patterns -- correct .NET 10 approach
- Regex processing order (fenced code blocks first, then images, then links, then inline formatting) prevents stripping inside code blocks
- Thread chaining logic is correct: first tweet has no reply field, subsequent tweets chain via in_reply_to_tweet_id
- firstTweetId ??= previousTweetId correctly captures the thread anchor
- ParseSegments correctly falls back to single-tweet mode for non-JSON content
- Sentence boundary detection scans backward from the limit, prefers sentence breaks over word breaks over hard cuts
- t.co URL budgeting (23 chars + 1 for newline separator) is correct per Twitter API docs
- Thread numbering with N/M suffix and length guard before appending
- Canonical URL appended to last segment with overflow to new segment if needed
- Token refresh via IOAuthService.RefreshTokenAsync uses Result<string> -- matches actual interface
- ValidateCredentialsAsync correctly routes through GetValidTokenAsync (fixes the HIGH-3 issue from LinkedIn review)
- 10-minute refresh window is appropriate for Twitter 2-hour token lifetime
- Test coverage: single tweet, thread chaining, reply IDs, first-tweet-as-anchor, token refresh, refresh failure, validation (valid/invalid), capabilities, draft/schedule rejection, rate limiting -- 11 connector tests, 8 formatter tests
- IDisposable on test class correctly disposes HttpClient and DbContext
- In-memory database with Guid.NewGuid() name prevents test interference
- SeedCredential method with cleanup of existing credentials before re-seeding -- good test isolation
- IPlatformConnector and IPlatformFormatter contracts fully implemented

**Verdict: BLOCK** -- Five high issues must be addressed before merge:
1. **HIGH-1** (dead _options injection) -- third connector with this pattern; resolve consistently across all three
2. **HIGH-2** (media upload advertised but not implemented) -- most important. Either implement or set SupportsImages: false
3. **HIGH-3** (anonymous type serialization bypasses naming policy) -- not a bug today but a maintenance trap
4. **HIGH-4** (token refresh window difference undocumented) -- add a comment explaining the choice
5. **HIGH-5** (partial thread failure silently discards posted tweets) -- unique to Twitter due to multi-request publishing

No critical security issues. The section correctly applies all patterns established in sections 07-08.