I now have a thorough understanding of the current codebase and what section-06 needs to cover. Let me generate the section content.

# Section 06: LinkedIn Image Upload

## Overview

This section extends `LinkedInPlatformAdapter` to support image uploads using LinkedIn's Images API. Currently, the adapter creates text-only posts via `POST /rest/posts`. After this section, the adapter detects when a `PlatformContent` carries a `MediaFile` and uploads the image binary to LinkedIn before including it in the post payload.

This section is backward compatible. Posts without images continue to work exactly as before.

## Dependencies

- **section-05-formatter-changes** must be completed first. That section modifies `LinkedInContentFormatter` to populate `PlatformContent.Media` with `MediaFile` entries when `Content.ImageFileId` is set. Without section 05, `PlatformContent.Media` will always be empty and the adapter's new image path will never activate.

## Background: LinkedIn Images API Flow

LinkedIn requires a three-step image upload process:

1. **Initialize upload** -- `POST /rest/images?action=initializeUpload` with `{ "initializeUploadRequest": { "owner": "urn:li:person:{id}" } }`. Returns an `uploadUrl` (a pre-signed PUT destination) and an `image` URN (`urn:li:image:{id}`).

2. **Upload binary** -- `PUT {uploadUrl}` with raw bytes (`Content-Type: application/octet-stream`). The `uploadUrl` is a fully-qualified URL (not relative to the API base), typically pointing to LinkedIn's blob storage.

3. **Wait for processing** -- Poll `GET /rest/images/{imageUrn}` until `status` is `"AVAILABLE"`. LinkedIn processes the image asynchronously; it can take a few seconds. Max 30 seconds with 2-second intervals.

Once the image URN status is `AVAILABLE`, it can be included in a post creation request via the `content.media` block.

### OAuth Scope

The existing `w_member_social` scope already covers image uploads. No scope changes are needed.

## Files to Create/Modify

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/LinkedInPlatformAdapter.cs` | Modify: add `UploadImageAsync`, modify `ExecutePublishAsync` |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInPlatformAdapterTests.cs` | Modify: add image upload and image-in-post tests |

## Tests (Write First)

All tests go in the existing test file at `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInPlatformAdapterTests.cs`. The test class already has mocks for `HttpMessageHandler`, `IMediaStorage`, and the full adapter constructor. Add these tests within the same class.

### Test: UploadImageAsync calls initializeUpload endpoint with correct owner URN

Verify that when `UploadImageAsync` is called, it sends a `POST` to `/rest/images?action=initializeUpload` with a JSON body containing `initializeUploadRequest.owner` equal to the author URN. The mock handler should return a successful response with `uploadUrl` and `image` fields. Assert the request was sent to the correct path with the correct method and body structure.

### Test: UploadImageAsync PUTs binary data to upload URL

After the initialize step returns an `uploadUrl`, verify a `PUT` request is sent to that exact URL with `Content-Type: application/octet-stream` and the raw byte array as the body. The mock handler needs to capture the second HTTP call (the PUT) and assert on its content.

### Test: UploadImageAsync polls image status until AVAILABLE

Mock the handler to return `"PROCESSING"` status for the first GET request to `/rest/images/{imageUrn}` and `"AVAILABLE"` for the second. Assert the method completes successfully and that two GET requests were made.

### Test: UploadImageAsync returns image URN on success

After the full flow (initialize, PUT, poll until AVAILABLE), assert the returned string matches the image URN from the initialize response (e.g., `"urn:li:image:C4D22AQH..."`)

### Test: UploadImageAsync throws after 30s polling timeout

Mock the status endpoint to always return `"PROCESSING"`. Use a short polling interval or mock time to verify that after exceeding the timeout, the method throws an appropriate exception or returns a failure result.

### Test: ExecutePublishAsync includes media block when PlatformContent has MediaFile

Create a `PlatformContent` with a non-empty `Media` list containing one `MediaFile`. Set up the handler to succeed for profile fetch, image upload flow, and post creation. Assert that the post creation JSON body includes the `content.media` block with `id` set to the image URN and `altText` from the `MediaFile`.

### Test: ExecutePublishAsync omits media block when PlatformContent has no MediaFile (backward compatible)

Create a `PlatformContent` with an empty `Media` list. Verify the post creation JSON body does NOT include a `content` or `content.media` field. This confirms the existing text-only behavior is unchanged.

### Test: Alt text is included in media block

Create a `PlatformContent` with a `MediaFile` that has `AltText = "AI generated image of cloud architecture"`. Assert the post JSON's `content.media.altText` matches this value.

### Test Stub Signatures

```csharp
// Add to LinkedInPlatformAdapterTests class

[Fact]
public async Task UploadImageAsync_CallsInitializeUpload_WithCorrectOwnerUrn()
{ /* Arrange: mock initializeUpload response. Act: call UploadImageAsync. Assert: POST to /rest/images?action=initializeUpload with owner URN */ }

[Fact]
public async Task UploadImageAsync_PutsBinaryData_ToUploadUrl()
{ /* Arrange: mock initialize + PUT responses. Act: call UploadImageAsync. Assert: PUT to uploadUrl with octet-stream */ }

[Fact]
public async Task UploadImageAsync_PollsImageStatus_UntilAvailable()
{ /* Arrange: mock PROCESSING then AVAILABLE. Act: call UploadImageAsync. Assert: two GET calls made */ }

[Fact]
public async Task UploadImageAsync_ReturnsImageUrn_OnSuccess()
{ /* Arrange: mock full flow. Act: call UploadImageAsync. Assert: returned URN matches */ }

[Fact]
public async Task UploadImageAsync_ThrowsOnTimeout_WhenImageNeverBecomeAvailable()
{ /* Arrange: mock status always PROCESSING. Act + Assert: exception after timeout */ }

[Fact]
public async Task ExecutePublishAsync_IncludesMediaBlock_WhenMediaFilePresent()
{ /* Arrange: PlatformContent with MediaFile. Act: PublishAsync. Assert: post JSON has content.media */ }

[Fact]
public async Task ExecutePublishAsync_OmitsMediaBlock_WhenNoMediaFile()
{ /* Arrange: PlatformContent with empty Media. Act: PublishAsync. Assert: post JSON has no content block */ }

[Fact]
public async Task ExecutePublishAsync_IncludesAltText_InMediaBlock()
{ /* Arrange: MediaFile with AltText. Act: PublishAsync. Assert: altText in post JSON */ }
```

## Implementation Details

### New Private Method: UploadImageAsync

Add a private method to `LinkedInPlatformAdapter`:

```csharp
/// <summary>
/// Uploads an image to LinkedIn via the Images API.
/// Returns the image URN (e.g., "urn:li:image:C4D22AQH...") on success.
/// </summary>
private async Task<Result<string>> UploadImageAsync(
    byte[] imageData, string authorUrn, string accessToken, CancellationToken ct)
```

**Step 1: Initialize Upload**

Send `POST /rest/images?action=initializeUpload` with:
```json
{
  "initializeUploadRequest": {
    "owner": "urn:li:person:{id}"
  }
}
```

Include the same auth and version headers used by `ExecutePublishAsync`:
- `Authorization: Bearer {token}`
- `X-Restli-Protocol-Version: 2.0.0`
- `Linkedin-Version: {apiVersion}`

Parse the response to extract:
- `value.uploadUrl` -- the pre-signed PUT destination
- `value.image` -- the image URN

On HTTP failure, return a `Result.Failure` using the existing `HandleHttpError` helper.

**Step 2: Upload Binary**

Send `PUT {uploadUrl}` with:
- `Content-Type: application/octet-stream`
- Body: raw `byte[]` as `ByteArrayContent`
- Same auth headers

The `uploadUrl` is a fully-qualified URL, not relative to the LinkedIn API base. Create the `HttpRequestMessage` with the absolute URI.

On failure, return `Result.Failure`.

**Step 3: Poll Until Available**

Poll `GET /rest/images/{imageUrn}` with 2-second intervals, up to 30 seconds total. The response contains a `status` field. When `status == "AVAILABLE"`, return the image URN. If the timeout expires with the image still in `"PROCESSING"` state, return `Result.Failure` with a descriptive error.

Use `Task.Delay(TimeSpan.FromSeconds(2), ct)` between polls for cancellation support.

**Constants:**

```csharp
private const int ImagePollIntervalSeconds = 2;
private const int ImagePollTimeoutSeconds = 30;
```

### Modify ExecutePublishAsync

The current method builds a text-only post JSON. Modify it to conditionally include the `content.media` block.

**Current flow:**
1. Get profile (author URN)
2. Build JSON with `author`, `commentary`, `visibility`, `distribution`, `lifecycleState`
3. POST to `/posts`

**Modified flow:**
1. Get profile (author URN)
2. Check if `content.Media` has any entries
3. If media present:
   a. Load image bytes via `MediaStorage.GetStreamAsync(mediaFile.FileId)` (the `MediaStorage` property is already accessible from `PlatformAdapterBase`)
   b. Call `UploadImageAsync(bytes, authorUrn, accessToken, ct)`
   c. If upload fails, return the failure
   d. Build post JSON **with** `content.media` block
4. If no media: build post JSON **without** `content.media` block (current behavior)
5. POST to `/posts`

**Post JSON with image:**

```json
{
  "author": "urn:li:person:{id}",
  "commentary": "Post text...",
  "visibility": "PUBLIC",
  "distribution": {
    "feedDistribution": "MAIN_FEED",
    "targetEntities": [],
    "thirdPartyDistributionChannels": []
  },
  "content": {
    "media": {
      "altText": "Description of the image",
      "id": "urn:li:image:C4D22AQH..."
    }
  },
  "lifecycleState": "PUBLISHED"
}
```

The key difference: the `content` field with a nested `media` object containing `id` (the image URN from upload) and `altText` (from `MediaFile.AltText`, defaulting to empty string if null).

### Reading Image Bytes from MediaStorage

The `PlatformAdapterBase` exposes `MediaStorage` as a protected property. To get the image bytes for upload:

```csharp
await using var stream = await MediaStorage.GetStreamAsync(mediaFile.FileId, ct);
using var ms = new MemoryStream();
await stream.CopyToAsync(ms, ct);
var imageBytes = ms.ToArray();
```

Only process the first `MediaFile` in the list. LinkedIn's post API supports a single image per post (multi-image requires a different API pattern). If `content.Media.Count > 1`, log a warning and use only the first entry.

### Error Handling

- **Initialize upload failure:** Return the HTTP error via `HandleHttpError`. The orchestrator will catch this and handle it at the pipeline level (blocking publish per the "image failure blocks publish" decision).
- **Binary upload failure:** Same pattern.
- **Polling timeout:** Return `Result.Failure(ErrorCode.InternalError, "LinkedIn image processing timed out after 30s")`.
- **MediaStorage failure:** If `GetStreamAsync` throws, let it propagate. The adapter does not catch storage exceptions; the publishing pipeline handles them.

### No Changes to Public Interface

`UploadImageAsync` is a private helper. The public contract (`ISocialPlatform.PublishAsync`) does not change. The adapter internally detects media presence and handles the upload transparently.

### JSON Serialization Note

The current `ExecutePublishAsync` uses anonymous types with `JsonContent.Create(new { ... })`. For the conditional `content.media` block, either:
- Use two separate anonymous type constructions (one with `content`, one without), or
- Build the JSON manually via `JsonObject` / `JsonNode`

The two-anonymous-type approach is simpler and maintains consistency with the existing code style. Create a helper method that builds the appropriate anonymous object based on whether an image URN is present.

## Verification Checklist

After implementation, verify:

1. All 8 new tests pass: `dotnet test --filter "LinkedInPlatformAdapterTests"`
2. Existing tests still pass (backward compatibility for text-only posts)
3. The `UploadImageAsync` method correctly handles the full 3-step flow
4. The post JSON includes `content.media` only when an image is present
5. The post JSON excludes `content` entirely when no image is present (matching current behavior)
6. Timeout and error cases return `Result.Failure` (not exceptions) where appropriate
7. The `uploadUrl` PUT request uses an absolute URI (not relative to the API base)