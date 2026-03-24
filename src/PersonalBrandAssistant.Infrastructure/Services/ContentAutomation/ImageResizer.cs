using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;
using SkiaSharp;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;

public sealed class ImageResizer : IImageResizer
{
    private static readonly IReadOnlyDictionary<PlatformType, (int Width, int Height)> PlatformDimensions =
        new Dictionary<PlatformType, (int Width, int Height)>
        {
            [PlatformType.LinkedIn] = (1200, 627),
            [PlatformType.TwitterX] = (1200, 675),
            [PlatformType.Instagram] = (1080, 1080),
            [PlatformType.PersonalBlog] = (1200, 630),
        };

    private readonly IMediaStorage _mediaStorage;
    private readonly ILogger<ImageResizer> _logger;

    public ImageResizer(IMediaStorage mediaStorage, ILogger<ImageResizer> logger)
    {
        _mediaStorage = mediaStorage;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<PlatformType, string>> ResizeForPlatformsAsync(
        string sourceFileId, PlatformType[] platforms, CancellationToken ct)
    {
        if (platforms.Length == 0)
            return new Dictionary<PlatformType, string>();

        await using var sourceStream = await _mediaStorage.GetStreamAsync(sourceFileId, ct);
        using var sourceBitmap = SKBitmap.Decode(sourceStream)
            ?? throw new InvalidOperationException("Failed to decode source image");

        var result = new Dictionary<PlatformType, string>();

        foreach (var platform in platforms)
        {
            if (!PlatformDimensions.TryGetValue(platform, out var dims))
            {
                _logger.LogWarning("No dimensions configured for platform {Platform}, skipping", platform);
                continue;
            }

            var (targetWidth, targetHeight) = dims;
            var resizedBytes = CropAndResize(sourceBitmap, targetWidth, targetHeight);

            using var resizedStream = new MemoryStream(resizedBytes);
            var fileId = await _mediaStorage.SaveAsync(
                resizedStream,
                $"{platform.ToString().ToLowerInvariant()}-{sourceFileId}",
                "image/png",
                ct);

            result[platform] = fileId;
            _logger.LogInformation("Resized image for {Platform}: {Width}x{Height} -> {FileId}",
                platform, targetWidth, targetHeight, fileId);
        }

        return result;
    }

    private static byte[] CropAndResize(SKBitmap source, int targetWidth, int targetHeight)
    {
        var targetAspect = (double)targetWidth / targetHeight;
        int cropWidth, cropHeight;

        if ((double)source.Width / source.Height > targetAspect)
        {
            cropHeight = source.Height;
            cropWidth = (int)(source.Height * targetAspect);
        }
        else
        {
            cropWidth = source.Width;
            cropHeight = (int)(source.Width / targetAspect);
        }

        var cropX = (source.Width - cropWidth) / 2;
        var cropY = (source.Height - cropHeight) / 2;
        var sourceRect = new SKRectI(cropX, cropY, cropX + cropWidth, cropY + cropHeight);
        var destRect = new SKRect(0, 0, targetWidth, targetHeight);

        using var targetBitmap = new SKBitmap(targetWidth, targetHeight);
        using var canvas = new SKCanvas(targetBitmap);
        using var paint = new SKPaint();

        // Extract the crop region first, then resize
        using var cropped = new SKBitmap(cropWidth, cropHeight);
        source.ExtractSubset(cropped, sourceRect);

        using var resized = cropped.Resize(new SKImageInfo(targetWidth, targetHeight), new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized is null)
            throw new InvalidOperationException("Failed to resize image");

        canvas.DrawBitmap(resized, 0, 0, paint);

        using var image = SKImage.FromBitmap(targetBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
