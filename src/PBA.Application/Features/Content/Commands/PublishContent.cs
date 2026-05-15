using MediatR;
using PBA.Application.Common.Interfaces;
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
        IBlogConnector blogConnector) : IRequestHandler<Command, Result>
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

            string? publishedUrl = null;
            if (content.PrimaryPlatform == Platform.Blog)
            {
                try
                {
                    publishedUrl = await blogConnector.PublishAsync(content, cancellationToken);
                }
                catch (Exception)
                {
                    return Result.Fail("Failed to publish to blog platform");
                }
            }

            db.ContentPlatformPublishes.Add(new ContentPlatformPublish
            {
                ContentId = request.ContentId,
                Platform = content.PrimaryPlatform,
                Status = PublishStatus.Published,
                PublishedUrl = publishedUrl,
                PublishedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }
}
