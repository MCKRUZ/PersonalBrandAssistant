# Section 04: Media Storage

## Overview

This section implements the `LocalMediaStorage` service and `MediaEndpoints` for serving files. The media storage system provides persistent file storage with date-organized paths, MIME validation via magic bytes, size enforcement, and HMAC-signed URL generation for secure file serving (required by Instagram's media container API).

## Dependencies

- **Section 02 (Interfaces & Models):** Provides `IMediaStorage` interface and `MediaStorageOptions` record.

## Files to Create

| File | Project | Purpose |
|------|---------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Services/Platform/LocalMediaStorage.cs` | Infrastructure | IMediaStorage implementation |
| `src/PersonalBrandAssistant.Api/Endpoints/MediaEndpoints.cs` | Api | Minimal API endpoint for serving media with HMAC validation |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LocalMediaStorageTests.cs` | Tests | Unit tests for LocalMediaStorage |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/MediaEndpointsTests.cs` | Tests | Integration tests for media serving endpoint |

## Tests (Write First)

### LocalMediaStorageTests

Test file: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LocalMediaStorageTests.cs`

The test class should use a temporary directory (via `Path.GetTempPath()` + unique subfolder) cleaned up in `Dispose`. Construct `LocalMediaStorage` with `IOptions<MediaStorageOptions>` containing the temp base path and a test signing key.

```csharp
// Test: SaveAsync creates file in date-organized path ({basePath}/{yyyy}/{MM}/{guid}.{ext})
//   - Create a MemoryStream with known content, call SaveAsync with "test.jpg" and "image/jpeg"
//   - Assert returned fileId is non-empty
//   - Assert file exists on disk at expected date-organized path pattern
//   - Assert file content matches input stream

// Test: SaveAsync returns unique fileId
//   - Save two files with same name/content
//   - Assert fileIds are different (GUID-based)

// Test: SaveAsync validates MIME type against magic bytes
//   - Create stream with PNG magic bytes (0x89 0x50 0x4E 0x47) but pass mimeType "image/jpeg"
//   - Assert Result is failure with validation error about MIME mismatch
//   - Also test: unknown/unsupported magic bytes are rejected

// Test: SaveAsync rejects files exceeding size limit
//   - Create stream larger than configured max size (e.g., 50MB default)
//   - Assert Result is failure with appropriate error message

// Test: GetStreamAsync returns readable stream for existing file
//   - SaveAsync a file, then GetStreamAsync with returned fileId
//   - Assert stream is readable and content matches original

// Test: GetStreamAsync returns failure for non-existent fileId
//   - Call GetStreamAsync with a random GUID-based fileId
//   - Assert Result is failure with NotFound error

// Test: GetPathAsync returns correct filesystem path
//   - SaveAsync a file, then GetPathAsync with returned fileId
//   - Assert path ends with expected extension and file exists at that path

// Test: DeleteAsync removes file and returns true
//   - SaveAsync a file, then DeleteAsync with returned fileId
//   - Assert returns true
//   - Assert file no longer exists on disk

// Test: DeleteAsync returns false for non-existent file
//   - Call DeleteAsync with a random GUID-based fileId
//   - Assert returns false

// Test: GetSignedUrlAsync generates URL with HMAC token and expiry
//   - SaveAsync a file, then GetSignedUrlAsync with 1-hour expiry
//   - Assert URL contains /api/media/{fileId}, a "token" query param, and an "expires" query param
//   - Assert "expires" timestamp is approximately 1 hour in the future

// Test: GetSignedUrlAsync URL expires after specified duration
//   - Generate signed URL with very short expiry
//   - Parse the "expires" query param and confirm it is in the future but near-term
```

### MediaEndpointsTests

Test file: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/MediaEndpointsTests.cs`

Use `CustomWebApplicationFactory` to set up the test server.

```csharp
// Test: MediaEndpoints serves file when HMAC token is valid and not expired
//   - Save a file via IMediaStorage
//   - Generate a signed URL via GetSignedUrlAsync
//   - GET the URL via test HttpClient
//   - Assert 200 OK with correct Content-Type and file content

// Test: MediaEndpoints returns 403 when HMAC token is invalid
//   - GET /api/media/{fileId}?token=invalid-token&expires={future-timestamp}
//   - Assert 403 Forbidden

// Test: MediaEndpoints returns 403 when URL has expired
//   - GET /api/media/{fileId}?token={valid-token}&expires={past-timestamp}
//   - Assert 403 Forbidden

// Test: MediaEndpoints returns 404 for non-existent fileId
//   - Generate valid HMAC for a non-existent fileId
//   - GET the URL
//   - Assert 404 Not Found
```

## Implementation Details

### MediaStorageOptions

Defined in Section 02. Extended here with max file size:

```csharp
public class MediaStorageOptions
{
    public string BasePath { get; set; } = "./media";
    public string? SigningKey { get; set; }
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB default
}
```

### LocalMediaStorage Implementation

File: `src/PersonalBrandAssistant.Infrastructure/Services/Platform/LocalMediaStorage.cs`

**Constructor dependencies:**
- `IOptions<MediaStorageOptions>` for base path, signing key, and max file size
- `ILogger<LocalMediaStorage>` for structured logging

**Registered as singleton** in DI (Section 12).

**FileId format:** `{yyyy}-{MM}-{guid}.{ext}` -- encodes the relative path. Parse to reconstruct `{basePath}/{yyyy}/{MM}/{guid}.{ext}`.

**SaveAsync logic:**
1. Validate stream is not null/empty
2. Read first 8 bytes for magic byte detection
3. Validate magic bytes match claimed MIME type
4. Check stream length against `MaxFileSizeBytes`
5. Generate GUID, determine extension from MIME type
6. Create date directory `{basePath}/{yyyy}/{MM}/`
7. Write stream to `{basePath}/{yyyy}/{MM}/{guid}.{ext}`
8. Return fileId string `{yyyy}-{MM}-{guid}.{ext}`
9. Reset stream position after magic byte read before writing

**Magic byte mapping:**

| MIME Type | Magic Bytes | Offset |
|-----------|-------------|--------|
| image/jpeg | `FF D8 FF` | 0 |
| image/png | `89 50 4E 47` | 0 |
| image/gif | `47 49 46 38` | 0 |
| image/webp | `52 49 46 46` + `57 45 42 50` | 0, 8 |
| video/mp4 | `66 74 79 70` | 4 |

**GetSignedUrlAsync logic:**
1. Compute expiry timestamp as `DateTimeOffset.UtcNow + expiry`, convert to Unix epoch seconds
2. Compute HMAC-SHA256 over `{fileId}:{expiryEpoch}` using `SigningKey`
3. Base64Url-encode the HMAC
4. Return `/api/media/{fileId}?token={hmac}&expires={expiryEpoch}`

### MediaEndpoints

File: `src/PersonalBrandAssistant.Api/Endpoints/MediaEndpoints.cs`

Follow existing endpoint pattern (static class with `Map*Endpoints` extension method).

```csharp
public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/media").WithTags("Media");
        group.MapGet("/{fileId}", ServeMedia);
    }
}
```

**ServeMedia handler:**
1. Check expiry: if `DateTimeOffset.UtcNow > DateTimeOffset.FromUnixTimeSeconds(expires)`, return 403
2. Recompute HMAC over `{fileId}:{expires}` with signing key
3. Compare with `CryptographicOperations.FixedTimeEquals` (timing-safe)
4. If mismatch, return 403
5. Get stream from `IMediaStorage.GetStreamAsync(fileId)`
6. If not found, return 404
7. Determine content type from fileId extension
8. Return `Results.File(stream, contentType)`

**Security:**
- Use `CryptographicOperations.FixedTimeEquals` for HMAC comparison
- Check expiry BEFORE validating HMAC (fail fast)
- Do NOT require API key auth (uses HMAC-signed URLs instead)
- Set `Cache-Control: private, max-age={remaining-seconds}`

### MIME Type to Extension Mapping

| MIME Type | Extension |
|-----------|-----------|
| image/jpeg | .jpg |
| image/png | .png |
| image/gif | .gif |
| image/webp | .webp |
| video/mp4 | .mp4 |
| ~~video/quicktime~~ | ~~.mov~~ (removed — no magic byte detection) |
