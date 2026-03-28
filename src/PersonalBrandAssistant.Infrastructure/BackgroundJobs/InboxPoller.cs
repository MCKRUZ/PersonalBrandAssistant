using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class InboxPoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboxPoller> _logger;

    public InboxPoller(
        IServiceScopeFactory scopeFactory,
        ILogger<InboxPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PollAllPlatformsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inbox polling");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task PollAllPlatformsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var adapters = scope.ServiceProvider.GetServices<ISocialEngagementAdapter>();

        foreach (var adapter in adapters)
        {
            try
            {
                var latestItem = await context.SocialInboxItems
                    .Where(i => i.Platform == adapter.Platform)
                    .OrderByDescending(i => i.ReceivedAt)
                    .Select(i => (DateTimeOffset?)i.ReceivedAt)
                    .FirstOrDefaultAsync(ct);

                var result = await adapter.PollInboxAsync(latestItem, ct);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Inbox poll failed for {Platform}: {Errors}",
                        adapter.Platform, string.Join(", ", result.Errors));
                    continue;
                }

                var newItems = 0;
                foreach (var entry in result.Value!)
                {
                    var exists = await context.SocialInboxItems
                        .AnyAsync(i => i.Platform == adapter.Platform
                            && i.PlatformItemId == entry.PlatformItemId, ct);

                    if (exists) continue;

                    context.SocialInboxItems.Add(new SocialInboxItem
                    {
                        Platform = adapter.Platform,
                        ItemType = entry.ItemType,
                        AuthorName = entry.AuthorName,
                        AuthorProfileUrl = entry.AuthorProfileUrl,
                        Content = entry.Content,
                        SourceUrl = entry.SourceUrl,
                        PlatformItemId = entry.PlatformItemId,
                        ReceivedAt = entry.ReceivedAt,
                    });
                    newItems++;
                }

                if (newItems > 0)
                {
                    await context.SaveChangesAsync(ct);
                    _logger.LogInformation("Polled {Count} new inbox items for {Platform}",
                        newItems, adapter.Platform);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling inbox for {Platform}", adapter.Platform);
            }
        }
    }
}
