using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IImageGenerationService
{
    Task<ImageGenerationResult> GenerateAsync(string prompt, ImageGenerationOptions options, CancellationToken ct);
}
