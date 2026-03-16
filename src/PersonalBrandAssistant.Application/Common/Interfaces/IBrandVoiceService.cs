using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IBrandVoiceService
{
    Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct);
}
