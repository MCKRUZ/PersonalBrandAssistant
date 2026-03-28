using MediatR;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.GenerateDraft;

public sealed class GenerateDraftCommandHandler : IRequestHandler<GenerateDraftCommand, Result<string>>
{
    private readonly IContentPipeline _pipeline;

    public GenerateDraftCommandHandler(IContentPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public Task<Result<string>> Handle(GenerateDraftCommand request, CancellationToken cancellationToken)
    {
        return _pipeline.GenerateDraftAsync(request.ContentId, cancellationToken);
    }
}
