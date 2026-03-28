using MediatR;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateFromTopic;

public sealed class CreateFromTopicCommandHandler : IRequestHandler<CreateFromTopicCommand, Result<Guid>>
{
    private readonly IContentPipeline _pipeline;

    public CreateFromTopicCommandHandler(IContentPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<Result<Guid>> Handle(CreateFromTopicCommand request, CancellationToken cancellationToken)
    {
        var creationRequest = new ContentCreationRequest(
            request.ContentType,
            request.Topic,
            request.Outline,
            request.TargetPlatforms,
            request.ParentContentId,
            request.Parameters);

        return await _pipeline.CreateFromTopicAsync(creationRequest, cancellationToken);
    }
}
