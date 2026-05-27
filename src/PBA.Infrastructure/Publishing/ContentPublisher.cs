using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Publishing;

public sealed class ContentPublisher(
    IAppDbContext db,
    [FromKeyedServices(Platform.Blog)] IPlatformConnector blogConnector,
    ILogger<ContentPublisher> logger) : IContentPublisher
{
    public async Task PublishAsync(Guid contentId)
    {
        var content = await db.Contents.FindAsync(contentId);
        if (content is null)
        {
            logger.LogWarning("Content {ContentId} not found for scheduled publish", contentId);
            return;
        }

        if (content.Status != ContentStatus.Scheduled)
        {
            logger.LogWarning("Content {ContentId} is {Status}, skipping scheduled publish", contentId, content.Status);
            return;
        }

        PlatformPublishResult? result = null;
        if (content.PrimaryPlatform == Platform.Blog)
        {
            var request = new PlatformPublishRequest(
                Content: content,
                TransformedContent: content.Body,
                Tags: content.Tags.AsReadOnly(),
                CanonicalUrl: null,
                Mode: PublishMode.Publish,
                ScheduledAt: content.ScheduledAt);
            result = await blogConnector.PublishAsync(request, CancellationToken.None);

            if (!result.Success)
            {
                db.ContentPlatformPublishes.Add(new ContentPlatformPublish
                {
                    ContentId = contentId,
                    Platform = content.PrimaryPlatform,
                    Status = PublishStatus.Failed,
                    ErrorMessage = result.ErrorMessage,
                    PublishedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
                logger.LogWarning("Failed to publish content {ContentId} to {Platform}: {Error}",
                    contentId, content.PrimaryPlatform, result.ErrorMessage);
                return;
            }
        }

        var machine = ContentStateMachine.Create(content);
        await machine.FireAsync(ContentTrigger.Publish);

        db.ContentPlatformPublishes.Add(new ContentPlatformPublish
        {
            ContentId = contentId,
            Platform = content.PrimaryPlatform,
            Status = PublishStatus.Published,
            PublishedUrl = result?.PublishedUrl,
            PlatformPostId = result?.PlatformPostId,
            PublishedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        logger.LogInformation("Published content {ContentId} to {Platform}", contentId, content.PrimaryPlatform);
    }
}
