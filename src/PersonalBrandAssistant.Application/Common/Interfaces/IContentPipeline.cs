using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IContentPipeline
{
    Task<Result<Guid>> CreateFromTopicAsync(ContentCreationRequest request, CancellationToken ct);
    Task<Result<string>> GenerateOutlineAsync(Guid contentId, CancellationToken ct);
    Task<Result<string>> GenerateDraftAsync(Guid contentId, CancellationToken ct);
    Task<Result<string>> GeneratePlatformDraftAsync(Guid contentId, PlatformType platform, string parentBody, CancellationToken ct);
    Task<Result<BrandVoiceScore>> ValidateVoiceAsync(Guid contentId, CancellationToken ct);
    Task<Result<MediatR.Unit>> SubmitForReviewAsync(Guid contentId, CancellationToken ct);
}
