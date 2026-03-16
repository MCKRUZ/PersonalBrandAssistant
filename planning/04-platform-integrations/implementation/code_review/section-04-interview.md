# Section 04 - Code Review Interview Transcript

## Review Summary
- **Verdict:** BLOCK (3 CRITICAL, 4 HIGH, 1 MEDIUM)
- **Disposition:** Auto-fixed 4 items (security + correctness), let go of 2

## Auto-Fixes Applied

### CRITICAL: Path Traversal in ResolvePath
- **Action:** Added regex validation (`FileIdPattern`) for fileId format + `Path.GetFullPath` canonicalization + `StartsWith` check against base path
- **Rationale:** Prevents `../` injection attacks to read arbitrary files

### CRITICAL: Broken MOV Support
- **Action:** Removed `video/quicktime` from both `MimeToExtension` and `ExtensionToContentType`
- **Rationale:** No magic bytes defined for MOV; all .mov uploads would fail validation. Can be re-added with proper magic byte detection later.

### HIGH: No File Cleanup on Write Failure
- **Action:** Wrapped `CopyToAsync` in try/catch with best-effort `File.Delete` in catch
- **Rationale:** Prevents partial files from accumulating on disk after write failures

### HIGH: Non-Seekable Stream Crash
- **Action:** Added `CanSeek` check at the start of `SaveAsync` with clear error message
- **Rationale:** `content.Length` and `content.Position = 0` throw on non-seekable streams (e.g., network streams)

## Items Let Go

### CRITICAL: HMAC Token in Query String
- **Decision:** Let go — this is the standard pattern for signed URLs (same as S3 presigned URLs, Azure SAS tokens). The URLs are server-to-server (Instagram fetches media from our endpoint). Not exposed to end users.

### MEDIUM: Duplicate ComputeHmac
- **Decision:** Let go — can be extracted to a shared utility when a third consumer appears. Current scope is small (2 files).

## Verification
- 10 LocalMediaStorage tests pass after fixes
- Pre-existing AgentEndpointsTests failures (12) are unrelated to this section
