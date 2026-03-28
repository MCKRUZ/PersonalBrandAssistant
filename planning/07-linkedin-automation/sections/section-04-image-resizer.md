The foundation section hasn't been written yet. That's fine -- the prompt says to reference dependencies without duplicating. Now I have all the context needed to write the section.

# Section 04: Image Resizer

## Overview

This section implements the `ImageResizer` service, a SkiaSharp-based utility that center-crops and downscales the AI-generated 1536x1536 source image to platform-optimal dimensions. Each resized variant is stored independently via `IMediaStorage`, and the service returns a dictionary mapping each `PlatformType` to its stored `fileId`.

The resizer is a pure image manipulation service with no business logic beyond crop math and media storage delegation. It is registered as a singleton in DI.

## Dependencies

- **section-01-foundation:** Provides the `IImageResizer` interface, `IMediaStorage` interface, `PlatformType` enum, and SkiaSharp NuGet package reference.

## File Paths

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/ImageResizer.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/ImageResizerTests.cs` | Create |

## Platform Dimension Specifications

Source image is always 1536x1536. All target dimensions are smaller, so only downscaling is needed (no upscale blur risk).

| Platform | Target Width | Target Height | Aspect Ratio | Crop Strategy |
|----------|-------------|--------------|--------------|---------------|
| LinkedIn | 1200 | 627 | ~1.91:1 | Center crop source to 1536x803, then downscale to 1200x627 |
| TwitterX | 1200 | 675 | 16:9 | Center crop source to 1536x864, then downscale to 1200x675 |
| Instagram | 1080 | 1080 | 1:1 | Downscale from 1536x1536 directly (already square) |
| PersonalBlog | 1200 | 630 | ~1.9:1 | Same crop strategy as LinkedIn |

The crop-then-downscale approach works as follows:
1. Calculate the target aspect ratio from the target width/height.
2. Given the 1536x1536 source, compute the largest rectangle at that aspect ratio that fits inside the source. For non-square targets, this means cropping height (since the source is square and targets are landscape). The crop width stays at 1536, and the crop height is `1536 / targetAspect` rounded to the nearest even number.
3. Center the crop rectangle vertically within the source image.
4. Downscale the cropped region to the final target dimensions.

## Tests First

Create `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/ImageResizerTests.cs`.

The test class needs a real (small) PNG image to test against. Use SkiaSharp to generate a 1536x1536 solid-color test image in the constructor rather than embedding a file. Mock `IMediaStorage` to capture what the resizer stores and return predictable file IDs.

### Test Stubs

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentAutomation;

/// <summary>
/// Tests for SkiaSharp-based ImageResizer.
/// Creates a real 1536x1536 PNG in-memory for each test.
/// Mocks IMediaStorage to capture saved streams and verify dimensions.
/// </summary>
public class ImageResizerTests : IDisposable
{
    // Constructor: create a real 1536x1536 SKBitmap, encode to PNG stream,
    // save via mock IMediaStorage to get a sourceFileId.
    // Mock IMediaStorage.GetStreamAsync(sourceFileId) to return the PNG stream.
    // Mock IMediaStorage.SaveAsync() to capture the saved stream, decode it
    // with SkiaSharp to verify dimensions, and return a predictable fileId
    // like "{platform}-resized".

    [Fact]
    public async Task ResizeForPlatformsAsync_LinkedIn_Returns1200x627()
    {
        /// Arrange: platforms = [LinkedIn]
        /// Act: call ResizeForPlatformsAsync
        /// Assert: the stream saved to IMediaStorage decodes to exactly 1200x627
        /// Assert: returned dictionary contains LinkedIn key
    }

    [Fact]
    public async Task ResizeForPlatformsAsync_TwitterX_Returns1200x675()
    {
        /// Arrange: platforms = [TwitterX]
        /// Act: call ResizeForPlatformsAsync
        /// Assert: saved image is exactly 1200x675
    }

    [Fact]
    public async Task ResizeForPlatformsAsync_Instagram_Returns1080x1080()
    {
        /// Arrange: platforms = [Instagram]
        /// Act: call ResizeForPlatformsAsync
        /// Assert: saved image is exactly 1080x1080
    }

    [Fact]
    public async Task ResizeForPlatformsAsync_PersonalBlog_Returns1200x630()
    {
        /// Arrange: platforms = [PersonalBlog]
        /// Act: call ResizeForPlatformsAsync
        /// Assert: saved image is exactly 1200x630
    }

    [Fact]
    public async Task ResizeForPlatformsAsync_ReturnsDictionaryMappingPlatformToFileId()
    {
        /// Arrange: platforms = [LinkedIn, TwitterX]
        /// Act: call ResizeForPlatformsAsync
        /// Assert: returned dictionary has exactly 2 entries
        /// Assert: each key matches a requested platform
        /// Assert: each value is a non-empty fileId
    }

    [Fact]
    public async Task ResizeForPlatformsAsync_CenterCrops_NonSquareTargets()
    {
        /// Arrange: create a 1536x1536 image with a known colored center region
        ///   (e.g., red 100x100 square at exact center, rest is blue)
        /// Act: resize for LinkedIn (1200x627 — landscape crop)
        /// Assert: the center pixel of the output is red, confirming center-crop
    }

    [Fact]
    public async Task ResizeForPlatformsAsync_StoresEachViaMediaStorage()
    {
        /// Arrange: platforms = [LinkedIn, TwitterX, Instagram]
        /// Act: call ResizeForPlatformsAsync
        /// Assert: IMediaStorage.SaveAsync called exactly 3 times
        /// Assert: each call uses "image/png" mime type
    }

    [Fact]
    public async Task ResizeForPlatformsAsync_EmptyPlatformArray_ReturnsEmptyDictionary()
    {
        /// Arrange: platforms = []
        /// Act: call ResizeForPlatformsAsync
        /// Assert: returned dictionary is empty
        /// Assert: IMediaStorage.SaveAsync never called
    }

    [Fact]
    public async Task ResizeForPlatformsAsync_OutputIsPngFormat()
    {
        /// Arrange: platforms = [LinkedIn]
        /// Act: call ResizeForPlatformsAsync
        /// Assert: the saved stream starts with PNG magic bytes (0x89 0x50 0x4E 0x47)
    }

    public void Dispose()
    {
        // Dispose any SkiaSharp resources (SKBitmap, etc.)
    }
}
```

### Key Testing Notes

- Do not mock SkiaSharp itself. The resizer tests should exercise real image manipulation to verify actual output dimensions. Use SkiaSharp's `SKBitmap` and `SKImage` in tests to decode the saved streams and inspect `Width`/`Height`.
- The `IMediaStorage` mock's `SaveAsync` callback should copy the incoming stream into a `MemoryStream` so the test can decode and inspect it after the call completes.
- The test project already references `PersonalBrandAssistant.Infrastructure`, so the SkiaSharp transitive dependency will be available. If not, add `SkiaSharp` to the test project's `.csproj`.

## Implementation Details

### `ImageResizer.cs`

Create `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/ImageResizer.cs`.

**Constructor dependencies:**
- `IMediaStorage _mediaStorage` -- for reading the source image and storing resized outputs
- `ILogger<ImageResizer> _logger`

**Singleton registration** (in section-01's DI setup): the resizer holds no state, only injected dependencies. `IMediaStorage` is itself scoped in the current codebase, but since `ImageResizer` only calls it during `ResizeForPlatformsAsync` (which is always invoked from a scoped context like the orchestrator), this is safe. If the DI container flags a captive dependency warning, register as scoped instead.

### Core Algorithm

The `ResizeForPlatformsAsync` method should:

1. **Load source image:** Call `_mediaStorage.GetStreamAsync(sourceFileId, ct)` to get the source stream. Decode it into an `SKBitmap` via `SKBitmap.Decode(stream)`. Validate that decoding succeeded (null check).

2. **Look up dimensions:** Use a static `IReadOnlyDictionary<PlatformType, (int Width, int Height)>` to map each platform to its target dimensions. This dictionary is defined as a private static field on the class, keeping the dimension specs in one place.

3. **For each platform:**
   a. Look up `(targetWidth, targetHeight)` from the dimensions dictionary. If a platform is not in the dictionary, log a warning and skip it.
   b. Calculate crop rectangle:
      - Compute `targetAspect = (double)targetWidth / targetHeight`
      - If source is wider relative to height than target aspect: `cropHeight = sourceHeight`, `cropWidth = (int)(sourceHeight * targetAspect)`
      - Otherwise: `cropWidth = sourceWidth`, `cropHeight = (int)(sourceWidth / targetAspect)`
      - Center the crop: `cropX = (sourceWidth - cropWidth) / 2`, `cropY = (sourceHeight - cropHeight) / 2`
   c. Crop: Create an `SKRectI` with the computed crop bounds. Use `SKBitmap.Resize()` is not appropriate for crop-then-scale. Instead:
      - Extract the crop region using `new SKBitmap()` + `source.ExtractSubset(cropped, cropRect)`, or use `SKCanvas` to draw the source with a source rect into a destination bitmap of the target size.
      - The recommended approach: create a target `SKBitmap(targetWidth, targetHeight)`, create an `SKCanvas` on it, then `canvas.DrawBitmap(source, sourceRect, destRect, paint)` where `sourceRect` is the crop rectangle and `destRect` is `(0, 0, targetWidth, targetHeight)`. Use `SKPaint` with `FilterQuality = SKFilterQuality.High` for quality downscaling.
   d. Encode to PNG: `resizedBitmap.Encode(SKEncodedImageFormat.Png, 90)` returns `SKData`. Wrap in a `MemoryStream`.
   e. Store: Call `_mediaStorage.SaveAsync(stream, $"{platform.ToString().ToLowerInvariant()}-{sourceFileId}", "image/png", ct)`.
   f. Add the returned `fileId` to the result dictionary.

4. **Dispose** the source `SKBitmap` and each intermediate `SKBitmap`/`SKCanvas`/`SKPaint` via `using` statements.

5. **Return** the `IReadOnlyDictionary<PlatformType, string>` result.

### Platform Dimensions Dictionary

```csharp
private static readonly IReadOnlyDictionary<PlatformType, (int Width, int Height)> PlatformDimensions =
    new Dictionary<PlatformType, (int Width, int Height)>
    {
        [PlatformType.LinkedIn] = (1200, 627),
        [PlatformType.TwitterX] = (1200, 675),
        [PlatformType.Instagram] = (1080, 1080),
        [PlatformType.PersonalBlog] = (1200, 630),
    };
```

### Error Handling

- If `SKBitmap.Decode` returns null, throw an `InvalidOperationException` with a message indicating the source image could not be decoded. The orchestrator (section-08) handles this as an image failure.
- If a platform is requested but not present in `PlatformDimensions`, log a warning and skip that platform. Do not throw -- partial results are acceptable at this layer. The orchestrator decides whether incomplete resizing is a failure.
- If `IMediaStorage.SaveAsync` throws for any individual platform resize, let the exception propagate. The orchestrator will catch it and treat it as an image failure (blocking publish).

### NuGet Dependency

The SkiaSharp package reference is added in section-01-foundation. The infrastructure `.csproj` will already have:

```xml
<PackageReference Include="SkiaSharp" Version="2.*" />
```

If running on Linux containers (Docker), also add `SkiaSharp.NativeAssets.Linux.NoDependencies` to avoid libSkiaSharp.so issues. This is a deployment concern documented here for awareness but implemented in section-01.

### Interface Contract (from section-01)

The `IImageResizer` interface lives at `src/PersonalBrandAssistant.Application/Common/Interfaces/IImageResizer.cs`:

```csharp
public interface IImageResizer
{
    Task<IReadOnlyDictionary<PlatformType, string>> ResizeForPlatformsAsync(
        string sourceFileId, PlatformType[] platforms, CancellationToken ct);
}
```

The implementation must match this signature exactly.

## Checklist

- [ ] Add `ImageResizerTests.cs` with all 8 test stubs
- [ ] Implement `ImageResizer.cs` with center-crop + downscale logic
- [ ] Verify all 4 platform dimensions produce correct output sizes
- [ ] Verify center-crop preserves the center of the source image
- [ ] Verify PNG output format (magic bytes)
- [ ] Verify empty platform array returns empty dictionary without calling storage
- [ ] Verify each resized image is stored via `IMediaStorage.SaveAsync` with `"image/png"` MIME type
- [ ] Run `dotnet test` -- all 8 tests pass