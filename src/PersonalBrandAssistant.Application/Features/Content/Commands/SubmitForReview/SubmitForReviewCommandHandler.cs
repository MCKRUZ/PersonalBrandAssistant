using MediatR;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.SubmitForReview;

public sealed class SubmitForReviewCommandHandler : IRequestHandler<SubmitForReviewCommand, Result<Unit>>
{
    private readonly IContentPipeline _pipeline;

    public SubmitForReviewCommandHandler(IContentPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public Task<Result<Unit>> Handle(SubmitForReviewCommand request, CancellationToken cancellationToken)
    {
        return _pipeline.SubmitForReviewAsync(request.ContentId, cancellationToken);
    }
}
