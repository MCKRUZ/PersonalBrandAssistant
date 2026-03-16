# Code Review: Section 04 - Media Storage

**Reviewer:** code-reviewer agent
**Date:** 2026-03-14
**Verdict:** BLOCK - Critical and High issues found

---

## CRITICAL Issues

### 1. Path Traversal via fileId in ResolvePath

**File:** LocalMediaStorage.cs - ResolvePath() (line ~213-225)

The fileId is split with a limit of 3, but the yyyy, mm, and guidAndExt segments are never validated. An attacker who controls the fileId (passed through the ServeMedia endpoint) can inject path traversal sequences. For example, parts could contain '..' values allowing escape from BasePath.

**Fix:** Validate that each segment matches the expected pattern (yyyy = 4 digits, mm = 2 digits, guidAndExt = GUID + extension) using regex. Then call Path.GetFullPath on the combined result and confirm it StartsWith the full BasePath (case-insensitive). This is defense-in-depth against both malformed fileIds and path traversal.
### 2. HMAC Token in Query String Leaks to Logs and Referrer Headers

**File:** MediaEndpoints.cs (line ~30, ~33-36)

The HMAC token and expiry are passed as query parameters. These will appear in web server access logs, proxy/CDN logs, Referer headers if the served media page links elsewhere, and browser history.

For a self-hosted service serving to the Instagram API, this is somewhat mitigated since Instagram is the consumer, not a browser. However, this is still a concern if the URLs are ever used in browser contexts.

**Fix:** Add a Referrer-Policy: no-referrer header on the media response and document that these URLs are intended only for server-to-server use.

### 3. video/quicktime (.mov) Has No Magic Byte Validation

**File:** LocalMediaStorage.cs (line ~119-136)

The MimeToExtension dictionary includes video/quicktime -> .mov, but the MagicBytes dictionary has no entry for it. This means .mov files will always fail magic byte validation in SaveAsync, since ValidateMagicBytes returns false when the MIME type is not found in the dictionary.

**Fix:** Either add magic byte signatures for MOV files (QuickTime uses ftyp box like MP4 at offset 4), or remove video/quicktime from MimeToExtension until validation is supported.

---

## HIGH Issues

### 4. No File Cleanup on Write Failure

**File:** LocalMediaStorage.cs - SaveAsync() (line ~168-169)

If CopyToAsync fails partway through (network error, disk full, cancellation), a partial file is left on disk. There is no try/catch to clean up the created file.

**Fix:** Wrap the FileStream creation and CopyToAsync in a try/catch. In the catch block, attempt File.Delete(filePath) as best-effort cleanup, then re-throw.

### 5. FixedTimeEquals Operates on Variable-Length Inputs

**File:** MediaEndpoints.cs (line ~48-51)

CryptographicOperations.FixedTimeEquals only provides timing-attack protection when both spans are the same length. When lengths differ, it returns false immediately, leaking the length. Since the HMAC output is always the same length (base64url of SHA-256 = 43 chars), this is mitigated in practice -- but only if the attacker always provides exactly 43 characters.

This is acceptable for now since it only leaks that the token length is wrong, but worth documenting the assumption.

### 6. Stream Returned from GetStreamAsync Could Leak on Exception

**File:** MediaEndpoints.cs (line ~56, 61)

Results.Stream() takes ownership of the stream and disposes it, which is correct for ASP.NET Core. However, the FileStream could leak if an exception occurs between stream creation and the Results.Stream() call.

**Fix:** Assign stream to a nullable variable outside a try block, call GetStreamAsync inside try, and in the catch block dispose the stream before re-throwing.

### 7. SaveAsync Accesses content.Length on Potentially Non-Seekable Streams

**File:** LocalMediaStorage.cs (line ~146)

content.Length throws NotSupportedException for non-seekable streams (e.g., network streams). The method also calls content.Position = 0 on line 153, which has the same issue. The interface accepts Stream, not MemoryStream, so callers could pass non-seekable streams.

**Fix:** Add a guard at the top of the method: if (!content.CanSeek) throw new ArgumentException.
---

## MEDIUM Issues

### 8. remaining Variable Computed But Never Used

**File:** MediaEndpoints.cs (line ~60)

The variable remaining is computed (DateTimeOffset subtraction) but never referenced. It appears intended for a Cache-Control header that was never added.

**Fix:** Either use it for cache headers or remove the dead code.

### 9. Mutable Static Dictionaries

**File:** MediaEndpoints.cs (line ~17), LocalMediaStorage.cs (lines ~119, ~128)

The static dictionaries use Dictionary rather than FrozenDictionary or IReadOnlyDictionary. While effectively treated as immutable, using mutable types leaves them open to accidental mutation.

**Fix:** Use FrozenDictionary (.NET 8+) for guaranteed immutability and better read performance.

### 10. GetPathAsync and DeleteAsync Are Synchronous But Return Task

**File:** LocalMediaStorage.cs (lines ~185-202)

These methods use Task.FromResult wrapping synchronous file I/O (File.Exists, File.Delete). This blocks the thread pool thread. For local storage this is a minor concern, but worth noting for consistency.

### 11. Test Does Not Verify FileId Format Structure

**File:** LocalMediaStorageTests.cs

The tests verify that fileId ends with .jpg and is non-empty, but never validate the full yyyy-MM-guid.ext format. This makes it possible for the format to drift without test failures.

**Fix:** Add a regex assertion to validate the full format pattern.

### 12. Missing Test: Signed URL Verification Round-Trip

**File:** LocalMediaStorageTests.cs

There is no test that generates a signed URL and then verifies the HMAC token matches what MediaEndpoints.ServeMedia would validate. The HMAC computation exists in two places (LocalMediaStorage.ComputeHmac and MediaEndpoints.ComputeHmac) and they must stay in sync.

**Fix:** Add an integration test via CustomWebApplicationFactory that saves a file, generates a signed URL, and GETs it expecting a 200.

### 13. Duplicate ComputeHmac Implementation

**File:** MediaEndpoints.cs (line ~69-75) and LocalMediaStorage.cs (line ~227-233)

The HMAC computation logic is duplicated across two files. If one is modified without the other, signed URLs will break silently.

**Fix:** Extract to a shared static utility class in the Application layer (e.g., Application/Common/Security/HmacSigner.cs) with a single static ComputeHmac method that both MediaEndpoints and LocalMediaStorage call.

---

## SUGGESTIONS

### 14. Consider URL-Encoding the fileId in Signed URLs

**File:** LocalMediaStorage.cs - GetSignedUrlAsync (line ~209)

The fileId contains dots and hyphens which are safe in URLs, but if the format ever changes to include other characters, the URL could break.

### 15. Consider Adding Rate Limiting to the Media Endpoint

**File:** MediaEndpoints.cs (line ~30)

The endpoint is AllowAnonymous. Per project security rules, rate limiting should be applied to public endpoints.

### 16. webp Magic Byte Check Is Incomplete

**File:** LocalMediaStorage.cs (line ~124)

WebP files start with RIFF (bytes 52 49 46 46) but also require WEBP at offset 8. The current check only verifies RIFF, which would also match WAV and AVI files.

**Fix:** Validate both RIFF at offset 0 and WEBP at offset 8 as a compound check.

---

## Summary

| Priority | Count | Key Concerns |
|----------|-------|-------------|
| CRITICAL | 3 | Path traversal, token leakage, broken MOV validation |
| HIGH | 4 | No cleanup on failure, stream safety, non-seekable stream crash |
| MEDIUM | 6 | Dead code, mutable statics, duplicate HMAC, missing tests |
| SUGGESTION | 3 | URL encoding, rate limiting, WebP validation |

**Verdict: BLOCK** -- The path traversal vulnerability in ResolvePath (issue #1) and the broken MOV support (issue #3) must be fixed before merge. The duplicate ComputeHmac (issue #13) is a maintenance hazard that should be addressed in this PR.
