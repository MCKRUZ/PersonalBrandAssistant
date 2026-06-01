# Section 07 Code Review Interview

## Auto-fixes Applied

### CRITICAL-1: Per-request auth headers instead of DefaultRequestHeaders
- Removed all DefaultRequestHeaders.Authorization mutations
- GetUserIdAsync now takes token parameter and sets Authorization per-request
- PublishAsync creates per-request Authorization header on each HttpRequestMessage

### CRITICAL-2: Generic error message in catch-all
- Changed from raw ex.Message to generic user-facing message
- Exception still logged via ILogger for debugging

### HIGH-1: IOptionsMonitor<MediumOptions> instead of IOptions
- Changed to IOptionsMonitor<MediumOptions> for consistency with codebase convention
- Access _options.CurrentValue at point of use

### HIGH-2: Use DefaultPublishStatus from options
- Default case in PublishMode switch now reads _options.CurrentValue.DefaultPublishStatus
- Options dependency is no longer dead code

### HIGH-3: Remove _cachedUserId
- Removed useless cache (connector is scoped, one instance per request)
- Simplifies code with no performance impact

### HIGH-4: Differentiate status codes in GetUserIdAsync
- 401 returns null (auth failure, user-friendly message)
- Other non-success statuses throw HttpRequestException (transient, hits catch block)

### LOW-5: Log in ValidateCredentialsAsync catch
- Added LogWarning for credential validation failures

## Let Go

### MEDIUM-1: Schedule-to-draft downgrade
- By design. PlatformCapabilities.SupportsScheduling = false signals callers.
- ContentPublisher checks capabilities before calling. Defense-in-depth not needed here.

### MEDIUM-2/3: SVG regex and naive string replace
- Intentionally simple. These handle the 99% case for markdown image references.
- Over-engineering the regex would add complexity with no practical benefit for a brand assistant.

### MEDIUM-4/5: Test helper and duplication
- Acceptable test duplication. Each test is self-contained and readable.
- Removed the unused SetupMeAndPostWithCapture helper.

### All NITPICKs
- Accepted as-is. Minor style issues don't warrant changes.
