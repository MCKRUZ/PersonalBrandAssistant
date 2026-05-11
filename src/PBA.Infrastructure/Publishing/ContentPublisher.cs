using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Publishing;

public sealed class ContentPublisher(
    IAppDbContext db,
    IBlogConnector blogConnector,
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

        string? publishedUrl = null;
        if (content.PrimaryPlatform == Platform.Blog)
            publishedUrl = await blogConnector.PublishAsync(content, CancellationToken.None);

        var machine = ContentStateMachine.Create(content);
        await machine.FireAsync(ContentTrigger.Publish);

        db.ContentPlatformPublishes.Add(new ContentPlatformPublish
        {
            ContentId = contentId,
            Platform = content.PrimaryPlatform,
            Status = PublishStatus.Published,
            PublishedUrl = publishedUrl,
            PublishedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        logger.LogInformation("Published content {ContentId} to {Platform}", contentId, content.PrimaryPlatform);
    }
}
