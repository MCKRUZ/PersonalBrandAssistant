using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class RetryFailedProcessor : BackgroundService
{
    private const int MaxRetries = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<RetryFailedProcessor> _logger;

    public RetryFailedProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<RetryFailedProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessRetryableContentAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during retry failed processing");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task ProcessRetryableContentAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IPublishingPipeline>();
        var workflowEngine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = _dateTimeProvider.UtcNow;

        var retryableContent = await context.Contents
            .Where(c => c.Status == ContentStatus.Failed
                        && c.RetryCount < MaxRetries
                        && c.NextRetryAt != null
                        && c.NextRetryAt <= now)
            .ToListAsync(ct);

        foreach (var content in retryableContent)
        {
            content.PublishingStartedAt = now;
            var transitionResult = await workflowEngine.TransitionAsync(
                content.Id, ContentStatus.Publishing, "Retry attempt", ActorType.System, ct);

            if (!transitionResult.IsSuccess)
            {
                _logger.LogWarning("Failed to transition content {ContentId} to Publishing for retry: {Errors}",
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
                content.NextRetryAt = now.Add(ScheduledPublishProcessor.GetBackoffDelay(content.RetryCount - 1));
                await workflowEngine.TransitionAsync(
                    content.Id, ContentStatus.Failed, publishResult.Errors.FirstOrDefault(), ActorType.System, ct);
            }
        }

        // Notify for exhausted retries
        var exhaustedContent = await context.Contents
            .Where(c => c.Status == ContentStatus.Failed && c.RetryCount >= MaxRetries)
            .ToListAsync(ct);

        foreach (var content in exhaustedContent)
        {
            var alreadyNotified = await context.Notifications
                .AnyAsync(n => n.ContentId == content.Id
                               && n.Type == NotificationType.ContentFailed, ct);

            if (!alreadyNotified)
            {
                await notificationService.SendAsync(
                    NotificationType.ContentFailed,
                    $"Content {content.Id} failed after {MaxRetries} attempts",
                    $"Content \"{content.Title ?? content.Id.ToString()}\" has exhausted all retry attempts.",
                    content.Id, ct);
            }
        }
    }
}
