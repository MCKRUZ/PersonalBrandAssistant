using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class PublishCompletionPoller : BackgroundService
{
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<PublishCompletionPoller> _logger;

    public PublishCompletionPoller(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<PublishCompletionPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PollProcessingEntriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during {Processor} processing", nameof(PublishCompletionPoller));
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task PollProcessingEntriesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var adapters = scope.ServiceProvider.GetRequiredService<IEnumerable<ISocialPlatform>>();
        var now = _dateTimeProvider.UtcNow;

        var processingEntries = await db.ContentPlatformStatuses
            .Where(e => e.Status == PlatformPublishStatus.Processing)
            .ToListAsync(ct);

        if (processingEntries.Count == 0)
            return;

        foreach (var entry in processingEntries)
        {
            if (now - entry.CreatedAt > ProcessingTimeout)
            {
                entry.Status = PlatformPublishStatus.Failed;
                entry.ErrorMessage = "Processing timed out after 30 minutes";
                _logger.LogWarning("Processing timed out for {ContentId} on {Platform}",
                    entry.ContentId, entry.Platform);
                continue;
            }

            var adapter = adapters.FirstOrDefault(a => a.Type == entry.Platform);
            if (adapter is null)
            {
                _logger.LogWarning("No adapter found for platform {Platform}", entry.Platform);
                continue;
            }

            var statusResult = await adapter.CheckPublishStatusAsync(entry.PlatformPostId!, ct);
            if (!statusResult.IsSuccess)
            {
                _logger.LogWarning("Failed to check publish status for {ContentId} on {Platform}: {Errors}",
                    entry.ContentId, entry.Platform, string.Join(", ", statusResult.Errors));
                continue;
            }

            var check = statusResult.Value!;
            if (check.Status == PlatformPublishStatus.Published)
            {
                entry.Status = PlatformPublishStatus.Published;
                entry.PublishedAt = now;
                if (check.PostUrl is not null)
                    entry.PostUrl = check.PostUrl;

                _logger.LogInformation("Processing completed for {ContentId} on {Platform}",
                    entry.ContentId, entry.Platform);
            }
            else if (check.Status == PlatformPublishStatus.Failed)
            {
                entry.Status = PlatformPublishStatus.Failed;
                entry.ErrorMessage = check.ErrorMessage ?? "Processing failed on platform";
                _logger.LogWarning("Processing failed for {ContentId} on {Platform}: {Error}",
                    entry.ContentId, entry.Platform, check.ErrorMessage);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
