using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;

namespace PBA.Application.Tests.Features.Feed;

internal static class FeedTestHelpers
{
    public static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    public static FeedItem CreateFeedItem(
        FeedItemType type = FeedItemType.AgentDraft,
        FeedItemPriority priority = FeedItemPriority.Normal,
        bool isRead = false,
        bool isActedOn = false,
        string? data = null,
        Guid? actionTargetId = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null) => new()
    {
        Type = type,
        Title = $"Test {type}",
        Summary = $"Summary for {type}",
        Data = data,
        ActionType = type switch
        {
            FeedItemType.AgentDraft => "approve",
            FeedItemType.TrendAlert => "view",
            FeedItemType.IdeaSuggestion => "create-content",
            FeedItemType.AnalyticsHighlight => "view",
            FeedItemType.ApprovalRequest => "approve",
            FeedItemType.SystemNotification => "view",
            _ => null
        },
        ActionTargetId = actionTargetId,
        Priority = priority,
        IsRead = isRead,
        IsActedOn = isActedOn,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        ExpiresAt = expiresAt
    };
}
