using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class PublishingPipelineStub : IPublishingPipeline
{
    public Task<Result<MediatR.Unit>> PublishAsync(Guid contentId, CancellationToken ct = default)
    {
        return Task.FromResult(
            Result<MediatR.Unit>.Failure(ErrorCode.InternalError, "Publishing pipeline not implemented"));
    }
}
