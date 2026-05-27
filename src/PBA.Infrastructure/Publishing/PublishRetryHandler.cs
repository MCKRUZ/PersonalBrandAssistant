using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Publishing;

public sealed class PublishRetryHandler(
    IAppDbContext db,
    IServiceProvider serviceProvider,
    IContentTransformer transformer,
    IBackgroundJobClient jobClient,
    ILogger<PublishRetryHandler> logger) : IPublishRetryHandler
{
    // [0] = initial retry (used by ContentPublisher), [1-2] = follow-up retries (used here)
    private static readonly TimeSpan[] BackoffDelays =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2)
    ];

    private const int MaxRetries = 3;

    public async Task RetryAsync(Guid publishRecordId, CancellationToken ct = default)
    {
        var record = await db.ContentPlatformPublishes
            .Include(p => p.Content)
            .FirstOrDefaultAsync(p => p.Id == publishRecordId, ct);

        if (record is null)
        {
            logger.LogWarning("Publish record {RecordId} not found for retry", publishRecordId);
            return;
        }

        if (record.Status == PublishStatus.Published)
        {
            logger.LogInformation("Publish record {RecordId} already published, skipping retry", publishRecordId);
            return;
        }

        if (record.Content is null)
        {
            logger.LogError("Content not found for publish record {RecordId}", publishRecordId);
            return;
        }

        var connector = serviceProvider.GetKeyedService<IPlatformConnector>(record.Platform);
        if (connector is null)
        {
            record.ErrorMessage = $"No connector registered for {record.Platform}";
            await db.SaveChangesAsync(ct);
            logger.LogError("No connector registered for platform {Platform}", record.Platform);
            return;
        }

        try
        {
            var transformed = await transformer.TransformAsync(record.Content, record.Platform, ct);

            var canonicalUrl = await db.ContentPlatformPublishes
                .Where(p => p.ContentId == record.ContentId
                         && p.Platform == record.Content.PrimaryPlatform
                         && p.Status == PublishStatus.Published)
                .Select(p => p.PublishedUrl)
                .FirstOrDefaultAsync(ct);

            var request = new PlatformPublishRequest(
                record.Content,
                transformed,
                record.Content.Tags.AsReadOnly(),
                canonicalUrl,
                PublishMode.Publish,
                null);

            var result = await connector.PublishAsync(request, ct);

            if (result.Success)
            {
                record.Status = PublishStatus.Published;
                record.PublishedUrl = result.PublishedUrl;
                record.PlatformPostId = result.PlatformPostId;
                record.PublishedAt = DateTimeOffset.UtcNow;
                record.NextRetryAt = null;
                logger.LogInformation("Retry succeeded for {Platform} publish {RecordId}", record.Platform, record.Id);
            }
            else
            {
                HandleFailure(record, result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Retry failed for {Platform} publish {RecordId}", record.Platform, record.Id);
            HandleFailure(record, ex.Message);
        }

        await db.SaveChangesAsync(ct);
    }

    private void HandleFailure(ContentPlatformPublish record, string errorMessage)
    {
        record.RetryCount++;
        record.Status = PublishStatus.Failed;
        record.ErrorMessage = errorMessage;

        if (record.RetryCount < MaxRetries)
        {
            var delay = BackoffDelays[record.RetryCount];
            record.NextRetryAt = DateTimeOffset.UtcNow + delay;

            jobClient.Schedule<IPublishRetryHandler>(
                x => x.RetryAsync(record.Id, CancellationToken.None), delay);

            logger.LogWarning(
                "Retry {Attempt}/{Max} failed for {Platform} publish {RecordId}, next retry in {Delay}",
                record.RetryCount, MaxRetries, record.Platform, record.Id, delay);
        }
        else
        {
            record.NextRetryAt = null;
            logger.LogError(
                "Max retries ({Max}) reached for {Platform} publish {RecordId}. Manual intervention required",
                MaxRetries, record.Platform, record.Id);
        }
    }
}
