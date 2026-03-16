using MediatR;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.ValidateVoice;

public sealed class ValidateVoiceCommandHandler : IRequestHandler<ValidateVoiceCommand, Result<BrandVoiceScore>>
{
    private readonly IContentPipeline _pipeline;

    public ValidateVoiceCommandHandler(IContentPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public Task<Result<BrandVoiceScore>> Handle(ValidateVoiceCommand request, CancellationToken cancellationToken)
    {
        return _pipeline.ValidateVoiceAsync(request.ContentId, cancellationToken);
    }
}
