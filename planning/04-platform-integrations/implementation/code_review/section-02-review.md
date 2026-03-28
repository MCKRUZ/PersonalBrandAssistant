# Code Review: Section 02 - Interfaces & Models (Platform Integrations)

**Reviewer:** code-reviewer agent
**Date:** 2026-03-14
**Verdict:** WARNING -- No CRITICAL issues. Several HIGH and MEDIUM issues to address before merging.

---

## HIGH Issues

### [HIGH-01] EngagementStats.PlatformSpecific uses mutable Dictionary
**File:** EngagementStats.cs
**Issue:** Records use reference equality for mutable collections. A mutable Dictionary in a record breaks the immutability guarantee and causes record equality to be reference-based for that field -- inconsistent with the rest of the record. This violates the project immutability-first coding style.

    // Current (bad)
    public record EngagementStats(
        int Likes, int Comments, int Shares, int Impressions, int Clicks,
        Dictionary<string, int> PlatformSpecific);

    // Fix
    public record EngagementStats(
        int Likes, int Comments, int Shares, int Impressions, int Clicks,
        IReadOnlyDictionary<string, int> PlatformSpecific);

### [HIGH-02] PlatformContent.Metadata uses mutable Dictionary
**File:** PlatformContent.cs
**Issue:** Same problem as HIGH-01. The Metadata dictionary is mutable, which breaks the immutability contract of the record and causes the equality test in PlatformContent_RecordEquality_WithSameValues to pass only by coincidence (same reference), not by value equality.

    // Current (bad)
    public record PlatformContent(
        string Text, string? Title, ContentType ContentType,
        IReadOnlyList<MediaFile> Media,
        Dictionary<string, string> Metadata);

    // Fix
    public record PlatformContent(
        string Text, string? Title, ContentType ContentType,
        IReadOnlyList<MediaFile> Media,
        IReadOnlyDictionary<string, string> Metadata);

### [HIGH-03] OAuthTokens stores AccessToken as plain string in a record
**File:** OAuthTokens.cs
**Issue:** Records auto-generate ToString() which will include the AccessToken and RefreshToken values. If this record is logged, serialized, or printed during debugging, tokens are exposed. This is a security concern -- tokens should never appear in logs.

**Options (pick one):**
1. Override ToString() to redact sensitive fields.
2. Make OAuthTokens a sealed class instead of a record, with a safe ToString() override.
3. Use a wrapper type like SensitiveString that redacts on ToString().

    // Option 1: Override ToString on the record
    public record OAuthTokens(
        string AccessToken, string? RefreshToken,
        DateTimeOffset? ExpiresAt, IReadOnlyList<string>? GrantedScopes)
    {
        public override string ToString() => "OAuthTokens { [REDACTED] }";
    }

### [HIGH-04] IMediaStorage.GetPathAsync leaks infrastructure file paths to Application layer
**File:** IMediaStorage.cs
**Issue:** The design brief states that MediaFile uses FileId (not FilePath) to avoid leaking infrastructure paths, yet GetPathAsync returns the raw file system path as a string. This breaks the abstraction -- Application-layer consumers should never need a raw path. If infrastructure code needs it, it should stay within the Infrastructure layer.

**Fix:** Remove GetPathAsync from the interface. If Infrastructure implementations need it internally, keep it as a private/internal method on the concrete class. GetStreamAsync and GetSignedUrlAsync already cover the legitimate use cases.

    public interface IMediaStorage
    {
        Task<string> SaveAsync(Stream content, string fileName, string mimeType, CancellationToken ct);
        Task<Stream> GetStreamAsync(string fileId, CancellationToken ct);
        Task<bool> DeleteAsync(string fileId, CancellationToken ct);
        Task<string> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct);
    }

### [HIGH-05] OAuthTokens.GrantedScopes uses mutable string[]
**File:** OAuthTokens.cs
**Issue:** Arrays are mutable. Callers can modify the scopes after construction, violating immutability. Use IReadOnlyList<string> instead.

    // Current
    public record OAuthTokens(string AccessToken, string? RefreshToken,
        DateTimeOffset? ExpiresAt, string[]? GrantedScopes);

    // Fix
    public record OAuthTokens(string AccessToken, string? RefreshToken,
        DateTimeOffset? ExpiresAt, IReadOnlyList<string>? GrantedScopes);

---

## MEDIUM Issues

### [MED-01] IMediaStorage return types are raw strings -- no Result wrapping
**File:** IMediaStorage.cs
**Issue:** Every other interface in this section uses Result<T> for error handling. IMediaStorage returns raw string, Stream, and bool values. If a file is not found or save fails, the only option is to throw an exception, which is inconsistent with the Result pattern used everywhere else.

    // Fix: wrap in Result<T> for consistency
    public interface IMediaStorage
    {
        Task<Result<string>> SaveAsync(Stream content, string fileName, string mimeType, CancellationToken ct);
        Task<Result<Stream>> GetStreamAsync(string fileId, CancellationToken ct);
        Task<Result<bool>> DeleteAsync(string fileId, CancellationToken ct);
        Task<Result<string>> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct);
    }

### [MED-02] MediaStorageOptions.SigningKey should not have a nullable default without validation
**File:** MediaStorageOptions.cs
**Issue:** SigningKey is nullable with no validation. If GetSignedUrlAsync is called and SigningKey is null, the behavior is undefined. Consider making it required or adding startup validation to ensure it is set when HMAC signing is enabled.

**Fix:** Add validation at registration time:

    services.AddOptions<MediaStorageOptions>()
        .BindConfiguration("MediaStorage")
        .ValidateDataAnnotations()
        .Validate(o => !string.IsNullOrEmpty(o.SigningKey),
            "SigningKey is required for HMAC URL signing");

### [MED-03] ISocialPlatform.ValidateContentAsync overlaps with IPlatformContentFormatter.FormatAndValidate
**File:** ISocialPlatform.cs, IPlatformContentFormatter.cs
**Issue:** There are two validation paths: IPlatformContentFormatter.FormatAndValidate returns Result<PlatformContent> (which can fail with validation errors), and ISocialPlatform.ValidateContentAsync returns Result<ContentValidation>. This creates ambiguity about which to call and when.

**Suggestion:** FormatAndValidate handles structural validation (character limits, media counts). ValidateContentAsync could be reserved for API-side validation (e.g., checking against platform rules that require an API call). If so, rename to ValidateWithPlatformAsync to make the distinction clear.

### [MED-04] PlatformOptions lacks ClientId / ClientSecret properties
**File:** PlatformIntegrationOptions.cs
**Issue:** OAuth flows require ClientId and ClientSecret per platform. These are absent from PlatformOptions. The IOAuthManager has no way to receive credentials unless they are handled elsewhere. If credentials are stored in a separate secure store, document that. Otherwise, add the properties here (they would bind from User Secrets / Key Vault, not hardcoded).

    public class PlatformOptions
    {
        public string ClientId { get; set; } = string.Empty;
        // ClientSecret should come from User Secrets (dev) / Key Vault (prod)
        public string ClientSecret { get; set; } = string.Empty;
        public string CallbackUrl { get; set; } = string.Empty;
        public string? BaseUrl { get; set; }
        public string? ApiVersion { get; set; }
        public int? DailyQuotaLimit { get; set; }
    }

### [MED-05] ISocialPlatform missing batch/scheduling capabilities
**File:** ISocialPlatform.cs
**Issue:** For a personal brand assistant, scheduling posts and batch operations are core use cases. Consider whether the interface should include:
- SchedulePostAsync(PlatformContent content, DateTimeOffset scheduledAt, CancellationToken ct)
- GetScheduledPostsAsync(CancellationToken ct)

If scheduling is handled at a higher orchestration layer, document that decision.

### [MED-06] IRateLimiter lacks YouTube daily quota tracking
**File:** IRateLimiter.cs
**Issue:** The design brief mentions YouTube daily quota tracking as a requirement, but IRateLimiter only tracks generic remaining calls and reset times. YouTube uses a quota-unit system (e.g., upload = 1600 units out of 10,000 daily). The current interface cannot represent this.

**Fix:** Add quota-aware fields to RateLimitStatus:

    public record RateLimitStatus(
        int? RemainingCalls,
        DateTimeOffset? ResetAt,
        bool IsLimited,
        int? QuotaUsed,      // For YouTube-style quota systems
        int? QuotaLimit);

---

## SUGGESTIONS

### [SUG-01] Interface existence tests provide minimal value
**File:** PlatformInterfacesTests.cs
**Issue:** Tests that verify interface members exist via reflection are compile-time checks disguised as tests. If a method is removed from the interface, any implementation will fail to compile, catching the issue earlier and more clearly than a test. These tests add maintenance burden without meaningful coverage.

**Recommendation:** Replace with behavioral tests -- mock the interfaces with concrete test doubles and verify the contract (e.g., a rate limiter that is exhausted returns Allowed=false with a RetryAt value). Those tests document expected behavior and catch logic errors.

### [SUG-02] PlatformContent equality test is misleading
**File:** PlatformIntegrationModelsTests.cs (PlatformContent_RecordEquality_WithSameValues test)
**Issue:** The test passes because both records reference the same media list and metadata dictionary instances. If you created two separate dictionaries with identical contents, record equality would fail for Dictionary (reference equality). This test gives false confidence. After fixing HIGH-01/HIGH-02 to use immutable types, rewrite this test with separate instances.

### [SUG-03] Consider adding RequiredScopes to PlatformOptions
**File:** PlatformIntegrationOptions.cs
**Issue:** Each platform requires specific OAuth scopes. Storing them in config makes scope management declarative and auditable.

    public IReadOnlyList<string> RequiredScopes { get; set; } = [];

### [SUG-04] MediaFile could benefit from a FileSize property
**File:** PlatformContent.cs
**Issue:** Platform APIs impose file size limits (Twitter: 5MB images, 512MB video; Instagram: 8MB photos). Having FileSize on MediaFile enables validation without re-reading the file from storage.

    public record MediaFile(string FileId, string MimeType, string? AltText, long? FileSizeBytes);

### [SUG-05] MediatR.Unit dependency in Application interfaces
**File:** IOAuthManager.cs, ISocialPlatform.cs
**Issue:** Using MediatR.Unit as a void return type couples the interface to MediatR. Consider defining a local Unit type or returning Result<bool> instead, keeping the Application layer free of library-specific types in its public contracts.

---

## Summary

| Priority | Count | Status |
|----------|-------|--------|
| CRITICAL | 0 | -- |
| HIGH | 5 | Must fix before merge |
| MEDIUM | 6 | Should fix |
| SUGGESTION | 5 | Consider |

**Verdict: WARNING** -- No critical security vulnerabilities, but HIGH issues around immutability violations (HIGH-01, HIGH-02, HIGH-05), token exposure in logging (HIGH-03), and abstraction leakage (HIGH-04) should be resolved before this section is merged. The immutability issues in particular conflict directly with the project stated coding standards.
