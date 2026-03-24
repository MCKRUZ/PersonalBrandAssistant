using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IImageResizer
{
    Task<IReadOnlyDictionary<PlatformType, string>> ResizeForPlatformsAsync(
        string sourceFileId, PlatformType[] platforms, CancellationToken ct);
}
