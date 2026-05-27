using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class PublishContent
{
    public record Command(Guid ContentId, IReadOnlyList<Platform>? TargetPlatforms = null) : IRequest<Result<PublishResult>>;

    internal sealed class Handler(
        IContentPublisher publisher) : IRequestHandler<Command, Result<PublishResult>>
    {
        public async Task<Result<PublishResult>> Handle(Command request, CancellationToken cancellationToken)
        {
            var result = await publisher.PublishAsync(request.ContentId, request.TargetPlatforms, cancellationToken);

            if (!result.PrimarySuccess)
                return Result<PublishResult>.Fail("Primary platform publish failed");

            return result;
        }
    }
}
