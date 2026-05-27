# Section 10 Code Review Interview

## Auto-fixes Applied

### HIGH-1: Nullable flow gap on draftId
**Fix:** Changed to pattern matching: `if (await CreateDraftAsync(...) is not { } draftId)`. Eliminates CS8604 risk.

### HIGH-2: PII in Debug logs
**Fix:** Response bodies moved to `LogTrace`. Status codes and byte lengths remain at `LogDebug`. This prevents PII exposure when developers enable Debug logging for troubleshooting.

### HIGH-3: send_email hardcoded to true
**Fix:** Added `SendEmailOnPublish` to `SubstackOptions` (defaults to `true`). Publish flow now reads `options.CurrentValue.SendEmailOnPublish`. Prevents accidental email blasts for backdated/corrected posts.

### MEDIUM-3: StripTrailingSections reverse iteration
**Fix:** Changed to forward iteration with `break` after first match. Clearer intent, same behavior.

### MEDIUM-7: Missing test for draft creation failure
**Fix:** Added `PublishAsync_DraftCreationFails_ReturnsFailure` — /api/v1/me succeeds but /api/v1/drafts returns 500.

### MEDIUM-8: Missing test for cookie decryption failure
**Fix:** Added `PublishAsync_CookieDecryptionFails_ReturnsFailure` — encryptor throws FormatException, verifies no HTTP calls made.

## Let Go

- HIGH-4: Redundant /api/v1/me calls (future optimization, not blocker)
- MEDIUM-1: PublicationSlug not used at runtime (consumed by DI registration in section-15)
- MEDIUM-2: DefaultAudience validation (can add later with FluentValidation)
- MEDIUM-4: Cookie value URL encoding (Substack session cookies are opaque tokens)
- MEDIUM-5: MediumOptions namespace inconsistency (informational, existing issue)
- MEDIUM-6: Schedule treated as Draft (spec explicitly says to do this)
- LOW-1 through LOW-5: Minor items, acceptable as-is
