# Section 12 Code Review: API Endpoints

## HIGH

### HIGH-1: Substack credential storage is a no-op
PlatformEndpoints.cs -- validates email/password but creates empty credential with no cookies/tokens stored.

### HIGH-2: Bare catch swallows all exceptions in OAuth callback
OAuthEndpoints.cs -- catch-all hides configuration errors, missing logging.

## MEDIUM

### MEDIUM-1: RetryPublishRequest.cs is dead code
Created but never referenced. Route parses platform from URL, not body.

### MEDIUM-2: PlatformPublishDto missing ErrorMessage, RetryCount, NextRetryAt
GetPublishStatus maps to DTO that omits failure details needed by frontend.

### MEDIUM-3: No FluentValidation for new DTOs
StoreCredentialsRequest, PublishContentRequest use inline validation instead of project pattern.

### MEDIUM-4: Status endpoints query IAppDbContext directly from handlers
Bypasses MediatR CQRS pattern used by ContentEndpoints.

### MEDIUM-5: Duplicate status logic between OAuth and Platform endpoints
Both compute Connected/Expired/NotConfigured independently.

### MEDIUM-6: Blog falls through to "uses OAuth" error message
Blog doesn't use OAuth; error message is misleading.

## LOW

### LOW-1: Missing test for Substack credential storage
### LOW-2: Missing test for OAuth callback with invalid state (403)
### LOW-3: Missing test for expired platform status
### LOW-4: Missing test for retry on valid failed record
### LOW-5: Anonymous objects in status responses
### LOW-6: Empty EncryptedAccessToken on non-OAuth credentials
