using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class ScheduledPublishProcessor : BackgroundService
{
    private static readonly TimeSpan[] BackoffSchedule = [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<ScheduledPublishProcessor> _logger;

    public ScheduledPublishProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<ScheduledPublishProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    internal static TimeSpan GetBackoffDelay(int retryCount)
    {
        var index = Math.Min(retryCount, BackoffSchedule.Length - 1);
        return BackoffSchedule[index];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDueContentAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled publish processing");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task ProcessDueContentAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IPublishingPipeline>();
        var workflowEngine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var now = _dateTimeProvider.UtcNow;

        var dueContent = await context.Contents
            .Where(c => c.Status == ContentStatus.Scheduled && c.ScheduledAt <= now)
            .ToListAsync(ct);

        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        foreach (var content in dueContent)
        {
            // For BlogPost content targeting PersonalBlog: create notification instead of auto-publishing
            if (content.ContentType == ContentType.BlogPost
                && content.TargetPlatforms.Contains(PlatformType.PersonalBlog))
            {
                var existingNotification = await context.UserNotifications
                    .AnyAsync(n => n.ContentId == content.Id
                        && n.Type == NotificationType.BlogReady.ToString()
                        && n.Status == NotificationStatus.Pending, ct);

                if (!existingNotification)
                {
                    await notificationService.SendAsync(
                        NotificationType.BlogReady,
                        "Blog post ready to publish",
                        $"Blog post \"{content.Title}\" is due for publishing. Confirm to deploy to personal blog.",
                        content.Id, ct);

                    _logger.LogInformation(
                        "Created BlogReady notification for content {ContentId}", content.Id);
                }
                continue;
            }

            content.PublishingStartedAt = now;
            var transitionResult = await workflowEngine.TransitionAsync(
                content.Id, ContentStatus.Publishing, null, ActorType.System, ct);

            if (!transitionResult.IsSuccess)
            {
                _logger.LogWarning("Failed to transition content {ContentId} to Publishing: {Errors}",
                    content.Id, string.Join(", ", transitionResult.Errors));
                continue;
            }

            var publishResult = await pipeline.PublishAsync(content.Id, ct);

            if (publishResult.IsSuccess)
            {
                await workflowEngine.TransitionAsync(
                    content.Id, ContentStatus.Published, null, ActorType.System, ct);
            }
            else
            {
                content.RetryCount++;
                content.NextRetryAt = now.Add(GetBackoffDelay(content.RetryCount - 1));
                await workflowEngine.TransitionAsync(
                    content.Id, ContentStatus.Failed, publishResult.Errors.FirstOrDefault(), ActorType.System, ct);
            }
        }
    }
}
