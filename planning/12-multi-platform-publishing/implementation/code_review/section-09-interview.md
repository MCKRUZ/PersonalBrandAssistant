# Section 09 Code Review Interview

## User Decisions

### HIGH-2: Media upload not implemented
**Decision:** Implement now.
**Action:** Added `UploadMediaAsync` method with full chunked flow (INIT/APPEND/FINALIZE/STATUS polling). Made `internal` since `Content` entity doesn't carry image bytes yet -- the method is callable from tests and ready for integration when `PlatformPublishRequest` is extended with image data. PublishAsync doesn't call it directly since the data pipeline doesn't pass images through yet. Added 2 tests for the upload flow.

### HIGH-5: Partial thread failure
**Decision:** Return partial success with context.
**Action:** On thread failure after first tweet is posted, return `Success=false` with the first tweet's ID, URL, and an error message like "Thread partially published (2/3 tweets)."

## Auto-fixes Applied

### HIGH-1: Dead `_options` field
**Fix:** Added enabled guard at top of PublishAsync: `if (!options.CurrentValue.Enabled) return failure`. Removed the unused `_options` field, using primary constructor parameter `options` directly.

### HIGH-3: Anonymous type serialization bypasses naming policy
**Fix:** Replaced anonymous types with strongly-typed records: `TweetPayload`, `TweetReply`, `TweetMedia`. The `JsonNamingPolicy.SnakeCaseLower` now handles all property name conversion.

### HIGH-4: Token refresh window undocumented
**Fix:** Added comment: `// Twitter access tokens expire after 2 hours; refresh early to avoid mid-thread failures`

### MEDIUM-1: Empty catch block
**Fix:** Added comment in catch: `// Content starts with '[' but is not a JSON thread array`

### MEDIUM-2: Single-element JSON array handling
**Fix:** Changed `Length: > 1` to `Length: > 0` in ParseSegments.

### MEDIUM-3: Sentence boundary at end of string
**Fix:** Changed boundary detection to also match sentence-end at the last character of the search region.

### Media response records
**Fix:** Added `TwitterMediaInitResponse`, `TwitterMediaFinalizeResponse`, `TwitterProcessingInfo` records for the chunked upload flow.

## Let Go

- MEDIUM-4: Thread numbering edge case (has length guard, acceptable)
- MEDIUM-5/6/7: Additional test coverage suggestions (22 tests already covers the spec well)
- LOW-1 through LOW-4: Minor inconsistencies, dead response records (kept for future use)
- All nitpicks
