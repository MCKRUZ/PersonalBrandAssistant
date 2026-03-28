using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.BlogServices;

internal sealed class BlogSchedulingService : IBlogSchedulingService
{
    private readonly IApplicationDbContext _db;
    private readonly INotificationService _notifications;
    private readonly PublishDelayOptions _options;
    private readonly ILogger<BlogSchedulingService> _logger;

    public BlogSchedulingService(
        IApplicationDbContext db,
        INotificationService notifications,
        IOptions<PublishDelayOptions> options,
        ILogger<BlogSchedulingService> logger)
    {
        _db = db;
        _notifications = notifications;
        _options = options.Value;
        _logger = logger;
    }

    public async Task OnSubstackPublicationConfirmedAsync(
        Guid contentId, DateTimeOffset substackPublishedAt, CancellationToken ct)
    {
        var content = await _db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null) return;

        if (content.BlogSkipped)
        {
            _logger.LogInformation("Blog publishing skipped for content {ContentId}", contentId);
            return;
        }

        if (!content.TargetPlatforms.Contains(PlatformType.PersonalBlog))
        {
            _logger.LogDebug("Content {ContentId} does not target PersonalBlog", contentId);
            return;
        }

        var delay = content.BlogDelayOverride ?? _options.DefaultSubstackToBlogDelay;
        var scheduledAt = substackPublishedAt + delay;

        if (_options.RequiresConfirmation)
        {
            await _notifications.SendAsync(
                NotificationType.BlogReady,
                "Blog publish ready",
                $"Blog post \"{content.Title}\" is scheduled for {scheduledAt:yyyy-MM-dd}. Confirm to proceed.",
                contentId,
                ct);

            _logger.LogInformation(
                "Blog scheduling notification sent for content {ContentId}, proposed date: {ScheduledAt}",
                contentId, scheduledAt);
        }
        else
        {
            await ScheduleBlogPlatformAsync(contentId, scheduledAt, ct);
        }
    }

    public async Task<Result<DateTimeOffset>> ConfirmBlogScheduleAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null)
            return Result<DateTimeOffset>.NotFound("Content not found");

        // Compute scheduled date from Substack publish time
        var substackStatus = await _db.ContentPlatformStatuses
            .FirstOrDefaultAsync(s => s.ContentId == contentId && s.Platform == PlatformType.Substack, ct);

        var substackPublishedAt = substackStatus?.PublishedAt ?? DateTimeOffset.UtcNow;
        var delay = content.BlogDelayOverride ?? _options.DefaultSubstackToBlogDelay;
        var scheduledAt = substackPublishedAt + delay;

        await ScheduleBlogPlatformAsync(contentId, scheduledAt, ct);

        return Result<DateTimeOffset>.Success(scheduledAt);
    }

    public async Task<Result<bool>> ValidateBlogPublishAllowedAsync(Guid contentId, CancellationToken ct)
    {
        var substackStatus = await _db.ContentPlatformStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ContentId == contentId && s.Platform == PlatformType.Substack, ct);

        if (substackStatus is null || substackStatus.Status != PlatformPublishStatus.Published)
            return Result<bool>.Failure(
                ErrorCode.ValidationFailed,
                "Substack must be published before blog can be published");

        return Result<bool>.Success(true);
    }

    private async Task ScheduleBlogPlatformAsync(Guid contentId, DateTimeOffset scheduledAt, CancellationToken ct)
    {
        var platformStatus = await _db.ContentPlatformStatuses
            .FirstOrDefaultAsync(s => s.ContentId == contentId && s.Platform == PlatformType.PersonalBlog, ct);

        if (platformStatus is null)
        {
            platformStatus = new Domain.Entities.ContentPlatformStatus
            {
                ContentId = contentId,
                Platform = PlatformType.PersonalBlog,
                Status = PlatformPublishStatus.Pending,
                ScheduledAt = scheduledAt
            };
            _db.ContentPlatformStatuses.Add(platformStatus);
        }
        else
        {
            platformStatus.Status = PlatformPublishStatus.Pending;
            platformStatus.ScheduledAt = scheduledAt;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Blog platform scheduled for content {ContentId} at {ScheduledAt}",
            contentId, scheduledAt);
    }
}
