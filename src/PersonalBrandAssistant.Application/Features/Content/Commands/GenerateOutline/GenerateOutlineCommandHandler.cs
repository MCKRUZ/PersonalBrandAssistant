using MediatR;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.GenerateOutline;

public sealed class GenerateOutlineCommandHandler : IRequestHandler<GenerateOutlineCommand, Result<string>>
{
    private readonly IContentPipeline _pipeline;

    public GenerateOutlineCommandHandler(IContentPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public Task<Result<string>> Handle(GenerateOutlineCommand request, CancellationToken cancellationToken)
    {
        return _pipeline.GenerateOutlineAsync(request.ContentId, cancellationToken);
    }
}
