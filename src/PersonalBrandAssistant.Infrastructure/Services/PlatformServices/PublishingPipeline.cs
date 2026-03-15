using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices;

public sealed class PublishingPipeline : IPublishingPipeline
{
    private readonly IApplicationDbContext _db;
    private readonly Dictionary<PlatformType, ISocialPlatform> _adapters;
    private readonly Dictionary<PlatformType, IPlatformContentFormatter> _formatters;
    private readonly IRateLimiter _rateLimiter;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PublishingPipeline> _logger;

    public PublishingPipeline(
        IApplicationDbContext db,
        IEnumerable<ISocialPlatform> adapters,
        IEnumerable<IPlatformContentFormatter> formatters,
        IRateLimiter rateLimiter,
        INotificationService notificationService,
        ILogger<PublishingPipeline> logger)
    {
        _db = db;
        _adapters = adapters.ToDictionary(a => a.Type);
        _formatters = formatters.ToDictionary(f => f.Platform);
        _rateLimiter = rateLimiter;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Result<MediatR.Unit>> PublishAsync(Guid contentId, CancellationToken ct = default)
    {
        var content = await _db.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result.NotFound<MediatR.Unit>($"Content '{contentId}' not found");

        var existingStatuses = await _db.ContentPlatformStatuses
            .Where(s => s.ContentId == contentId)
            .ToListAsync(ct);

        var succeeded = 0;
        var failed = 0;
        var failedPlatforms = new List<PlatformType>();

        foreach (var platform in content.TargetPlatforms)
        {
            var existingStatus = existingStatuses.FirstOrDefault(s => s.Platform == platform);

            // Idempotency: skip already Published or Processing
            if (existingStatus is { Status: PlatformPublishStatus.Published or PlatformPublishStatus.Processing })
            {
                succeeded++;
                continue;
            }

            try
            {
                var platformResult = await PublishToPlatformAsync(content, platform, existingStatus, ct);
                if (platformResult)
                    succeeded++;
                else
                {
                    failed++;
                    failedPlatforms.Add(platform);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogInformation("Concurrency conflict for {Platform} on content {ContentId}, another instance is handling it",
                    platform, contentId);
                succeeded++; // Another instance is processing this platform
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error publishing to {Platform} for content {ContentId}",
                    platform, contentId);
                failed++;
                failedPlatforms.Add(platform);
            }
        }

        // Determine overall status
        await TransitionContentStatusAsync(content, succeeded, failed, ct);

        // Notify on partial failure
        if (succeeded > 0 && failed > 0)
        {
            var platformNames = string.Join(", ", failedPlatforms);
            await _notificationService.SendAsync(
                NotificationType.ContentFailed,
                "Partial publish failure",
                $"Failed to publish to: {platformNames}",
                contentId, ct);
        }

        return succeeded > 0
            ? Result.Success(MediatR.Unit.Value)
            : Result.Failure<MediatR.Unit>(ErrorCode.InternalError, "All platforms failed to publish");
    }

    private async Task<bool> PublishToPlatformAsync(
        Content content, PlatformType platform,
        ContentPlatformStatus? existingStatus, CancellationToken ct)
    {
        // Create or reuse status record
        ContentPlatformStatus status;
        if (existingStatus is null)
        {
            status = new ContentPlatformStatus
            {
                ContentId = content.Id,
                Platform = platform,
                IdempotencyKey = ComputeIdempotencyKey(content.Id, platform, content.Version),
                Status = PlatformPublishStatus.Pending,
            };
            _db.ContentPlatformStatuses.Add(status);
        }
        else
        {
            status = existingStatus;
            status.Status = PlatformPublishStatus.Pending;
        }

        await _db.SaveChangesAsync(ct); // Acquire lease

        // Format content
        if (!_formatters.TryGetValue(platform, out var formatter))
        {
            status.Status = PlatformPublishStatus.Skipped;
            status.ErrorMessage = $"No formatter registered for {platform}";
            await _db.SaveChangesAsync(ct);
            return false;
        }

        var formatResult = formatter.FormatAndValidate(content);
        if (!formatResult.IsSuccess)
        {
            status.Status = PlatformPublishStatus.Skipped;
            status.ErrorMessage = string.Join("; ", formatResult.Errors);
            await _db.SaveChangesAsync(ct);
            return false;
        }

        // Check rate limit (fail-open: if rate limiter errors, proceed with publish)
        var rateLimitResult = await _rateLimiter.CanMakeRequestAsync(platform, "publish", ct);
        if (!rateLimitResult.IsSuccess)
            _logger.LogWarning("Rate limiter failed for {Platform}, proceeding with publish", platform);
        else if (!rateLimitResult.Value!.Allowed)
        {
            status.Status = PlatformPublishStatus.RateLimited;
            status.NextRetryAt = rateLimitResult.Value.RetryAt;
            status.ErrorMessage = rateLimitResult.Value.Reason;
            await _db.SaveChangesAsync(ct);
            return false;
        }

        // Publish
        if (!_adapters.TryGetValue(platform, out var adapter))
        {
            status.Status = PlatformPublishStatus.Failed;
            status.ErrorMessage = $"No adapter registered for {platform}";
            await _db.SaveChangesAsync(ct);
            return false;
        }

        var publishResult = await adapter.PublishAsync(formatResult.Value!, ct);

        if (publishResult.IsSuccess)
        {
            status.PlatformPostId = publishResult.Value!.PlatformPostId;
            status.PostUrl = publishResult.Value.PostUrl;
            status.PublishedAt = publishResult.Value.PublishedAt;
            status.Status = PlatformPublishStatus.Published;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        status.Status = PlatformPublishStatus.Failed;
        status.ErrorMessage = string.Join("; ", publishResult.Errors);
        status.RetryCount++;
        await _db.SaveChangesAsync(ct);
        return false;
    }

    private async Task TransitionContentStatusAsync(
        Content content, int succeeded, int failed, CancellationToken ct)
    {
        try
        {
            if (failed == 0 && succeeded > 0)
                content.TransitionTo(ContentStatus.Published);
            else if (succeeded == 0 && failed > 0)
                content.TransitionTo(ContentStatus.Failed);
            // Mixed results: stay in Publishing state

            await _db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Could not transition content {ContentId} status", content.Id);
        }
    }

    private static string ComputeIdempotencyKey(Guid contentId, PlatformType platform, uint version)
    {
        var input = $"{contentId}:{platform}:{version}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
