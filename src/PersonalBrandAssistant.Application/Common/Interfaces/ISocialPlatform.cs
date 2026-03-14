using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISocialPlatform
{
    PlatformType Type { get; }
    Task<Result<PublishResult>> PublishAsync(PlatformContent content, CancellationToken ct);
    Task<Result<Unit>> DeletePostAsync(string platformPostId, CancellationToken ct);
    Task<Result<EngagementStats>> GetEngagementAsync(string platformPostId, CancellationToken ct);
    Task<Result<PlatformProfile>> GetProfileAsync(CancellationToken ct);
    Task<Result<ContentValidation>> ValidateContentAsync(PlatformContent content, CancellationToken ct);
}
