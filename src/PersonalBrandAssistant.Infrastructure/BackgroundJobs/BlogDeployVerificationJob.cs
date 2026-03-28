using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

internal sealed class BlogDeployVerificationJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlogDeployVerificationJob> _logger;

    public BlogDeployVerificationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<BlogDeployVerificationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await VerifyPendingDeploymentsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blog deploy verification tick failed");
            }
        }
    }

    internal async Task VerifyPendingDeploymentsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IGitHubPublishService>();

        var pendingRequests = await db.BlogPublishRequests
            .Where(r => r.Status == BlogPublishStatus.Publishing && r.BlogUrl != null)
            .ToListAsync(ct);

        foreach (var request in pendingRequests)
        {
            request.VerificationAttempts++;

            if (request.VerificationAttempts > 10)
            {
                request.Status = BlogPublishStatus.Failed;
                request.ErrorMessage = "Deploy verification exhausted after 10 attempts";
                _logger.LogWarning(
                    "Deploy verification exhausted for content {ContentId}", request.ContentId);

                var platformStatus = await db.ContentPlatformStatuses
                    .FirstOrDefaultAsync(s => s.ContentId == request.ContentId
                        && s.Platform == PlatformType.PersonalBlog, ct);
                if (platformStatus is not null)
                {
                    platformStatus.Status = PlatformPublishStatus.Failed;
                    platformStatus.ErrorMessage = "Deploy verification exhausted";
                }

                await db.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                var deployed = await publisher.VerifyDeploymentAsync(request.BlogUrl!, ct);

                if (deployed)
                {
                    request.Status = BlogPublishStatus.Published;
                    _logger.LogInformation(
                        "Blog deploy verified for content {ContentId} at {Url}",
                        request.ContentId, request.BlogUrl);

                    var platformStatus = await db.ContentPlatformStatuses
                        .FirstOrDefaultAsync(s => s.ContentId == request.ContentId
                            && s.Platform == PlatformType.PersonalBlog, ct);
                    if (platformStatus is not null)
                    {
                        platformStatus.Status = PlatformPublishStatus.Published;
                        platformStatus.PostUrl = request.BlogUrl;
                        platformStatus.PublishedAt = DateTimeOffset.UtcNow;
                    }

                    await db.SaveChangesAsync(ct);
                }
                // If not deployed yet, leave in Publishing state for next tick
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Deploy verification attempt {Attempt} failed for content {ContentId}",
                    request.VerificationAttempts, request.ContentId);
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
