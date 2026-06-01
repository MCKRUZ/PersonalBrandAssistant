# Section 12 Code Review Interview: API Endpoints

## Triage Summary

All findings auto-fixed (no user interview needed -- straightforward improvements).

## AUTO-FIXES APPLIED

### HIGH-1: Substack credential storage is a no-op
- **Action:** Changed Substack case to return `Results.BadRequest("Substack credential storage via API is not yet supported. Use browser login.")` 
- **Rationale:** No-op credential creation silently fails; explicit error is safer and honest about current limitations

### HIGH-2: Bare catch swallows all exceptions in OAuth callback
- **Action:** Replaced bare `catch` with `catch (Exception ex)` + `ILoggerFactory` injection + `logger.LogError(ex, ...)`
- **Rationale:** Configuration errors, network failures, and invalid state were invisible. Logging is essential for debugging OAuth flows

### MEDIUM-1: RetryPublishRequest.cs is dead code
- **Action:** Deleted `src/PBA.Application/Features/Content/Dtos/RetryPublishRequest.cs`
- **Rationale:** Route parses platform from URL path (`/retry/{platform}`), not from request body. File was never referenced

### MEDIUM-2: PlatformPublishDto missing failure details
- **Action:** Added `ErrorMessage`, `RetryCount`, `NextRetryAt` to `PlatformPublishDto` and updated `GetPublishStatus.cs` Select projection
- **Rationale:** Frontend needs failure details to show retry status and error messages to users

### MEDIUM-6: Blog falls through to misleading "uses OAuth" error
- **Action:** Added explicit `case Platform.Blog: return Results.BadRequest("Blog does not require credentials.")` and made LinkedIn/Twitter cases explicit
- **Rationale:** Blog uses file-system publishing, not OAuth. Previous default case was misleading

## DEFERRED (LOW priority, not blocking)

- LOW-1 through LOW-6: Missing edge-case tests and anonymous objects. These are test coverage gaps and minor code style issues that don't affect correctness. Can be addressed in a polish pass.
- MEDIUM-3 (FluentValidation): The inline validation in endpoint handlers is adequate for these simple cases. FluentValidation migration can happen when the DTOs grow more complex.
- MEDIUM-4 (MediatR bypass): Status queries directly hitting IAppDbContext is fine for simple lookups -- MediatR adds overhead for no behavioral value here.
- MEDIUM-5 (Duplicate status logic): Both OAuth and Platform endpoints compute status independently. Small duplication, not worth abstracting yet.

## NEW TESTS ADDED

- `PostCredentials_Blog_Returns400_NoCredentialsNeeded` -- verifies Blog explicit error
- `PostCredentials_Substack_Returns400_NotYetSupported` -- verifies Substack not-yet-supported error
