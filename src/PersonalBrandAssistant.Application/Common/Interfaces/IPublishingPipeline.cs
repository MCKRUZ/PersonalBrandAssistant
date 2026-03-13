using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IPublishingPipeline
{
    Task<Result<MediatR.Unit>> PublishAsync(Guid contentId, CancellationToken ct = default);
}
