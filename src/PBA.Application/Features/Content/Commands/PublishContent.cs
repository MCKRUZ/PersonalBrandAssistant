using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class PublishContent
{
    public record Command(Guid ContentId) : IRequest<Result>;

    internal sealed class Handler(
        IAppDbContext db,
        [FromKeyedServices(Platform.Blog)] IPlatformConnector blogConnector) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
            if (content is null)
                return Result.NotFound($"Content {request.ContentId} not found");

            var machine = ContentStateMachine.Create(content);
            try
            {
                await machine.FireAsync(ContentTrigger.PublishNow);
            }
            catch (InvalidOperationException)
            {
                return Result.Fail("Cannot publish content in its current status");
            }

            PlatformPublishResult? result = null;
            if (content.PrimaryPlatform == Platform.Blog)
            {
                var publishRequest = new PlatformPublishRequest(
                    Content: content,
                    TransformedContent: content.Body,
                    Tags: content.Tags.AsReadOnly(),
                    CanonicalUrl: null,
                    Mode: PublishMode.Publish,
                    ScheduledAt: null);

                result = await blogConnector.PublishAsync(publishRequest, cancellationToken);
                if (!result.Success)
                    return Result.Fail(result.ErrorMessage ?? "Failed to publish to blog platform");
            }

            db.ContentPlatformPublishes.Add(new ContentPlatformPublish
            {
                ContentId = request.ContentId,
                Platform = content.PrimaryPlatform,
                Status = PublishStatus.Published,
                PublishedUrl = result?.PublishedUrl,
                PlatformPostId = result?.PlatformPostId,
                PublishedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }
}
