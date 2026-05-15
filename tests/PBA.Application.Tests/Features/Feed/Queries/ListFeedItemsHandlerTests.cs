using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Feed.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Queries;

public class ListFeedItemsHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FeedItem CreateFeedItem(
        string title = "Test Item",
        FeedItemType type = FeedItemType.SystemNotification,
        FeedItemPriority priority = FeedItemPriority.Normal,
        bool isRead = false,
        bool isActedOn = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        string? data = null)
    {
        return new FeedItem
        {
            Title = title,
            Type = type,
            Priority = priority,
            IsRead = isRead,
            IsActedOn = isActedOn,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            Data = data
        };
    }

    [Fact]
    public async Task Handle_DefaultQuery_ReturnsPaginatedResultsSortedByCreatedAtDesc()
    {
        await using var context = CreateContext();
        var baseTime = DateTimeOffset.UtcNow;
        for (var i = 0; i < 25; i++)
            context.FeedItems.Add(CreateFeedItem($"Item {i}", createdAt: baseTime.AddMinutes(-i)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value!.Items.Count);
        Assert.Equal(25, result.Value.TotalCount);
        Assert.Equal(2, result.Value.TotalPages);
        Assert.Equal("Item 0", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task Handle_TypeFilter_ReturnsMatchingOnly()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem("Alert 1", type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem("Alert 2", type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem("Draft 1", type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem("Notif 1", type: FeedItemType.SystemNotification));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query { Type = FeedItemType.TrendAlert }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, item => Assert.Equal(FeedItemType.TrendAlert, item.Type));
    }

    [Fact]
    public async Task Handle_PriorityFilter_ReturnsMatchingOnly()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem("High 1", priority: FeedItemPriority.High));
        context.FeedItems.Add(CreateFeedItem("High 2", priority: FeedItemPriority.High));
        context.FeedItems.Add(CreateFeedItem("Normal 1", priority: FeedItemPriority.Normal));
        context.FeedItems.Add(CreateFeedItem("Low 1", priority: FeedItemPriority.Low));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query { Priority = FeedItemPriority.High }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, item => Assert.Equal(FeedItemPriority.High, item.Priority));
    }

    [Fact]
    public async Task Handle_IsReadFilter_ReturnsMatchingOnly()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem("Unread 1", isRead: false));
        context.FeedItems.Add(CreateFeedItem("Unread 2", isRead: false));
        context.FeedItems.Add(CreateFeedItem("Read 1", isRead: true));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query { IsRead = false }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, item => Assert.False(item.IsRead));
    }

    [Fact]
    public async Task Handle_ExcludesExpiredItemsByDefault()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem("Active"));
        context.FeedItems.Add(CreateFeedItem("Also Active", expiresAt: DateTimeOffset.UtcNow.AddDays(1)));
        context.FeedItems.Add(CreateFeedItem("Expired", expiresAt: DateTimeOffset.UtcNow.AddHours(-1)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.DoesNotContain(result.Value.Items, item => item.Title == "Expired");
    }

    [Fact]
    public async Task Handle_IncludesExpiredItemsWhenRequested()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem("Active"));
        context.FeedItems.Add(CreateFeedItem("Expired", expiresAt: DateTimeOffset.UtcNow.AddHours(-1)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query { IncludeExpired = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    [Fact]
    public async Task Handle_NoMatchingItems_ReturnsEmptyPage()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem("Draft", type: FeedItemType.AgentDraft));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query { Type = FeedItemType.TrendAlert }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task Handle_RespectsPageSizeLimit()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 10; i++)
            context.FeedItems.Add(CreateFeedItem($"Item {i}"));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query { PageSize = 5 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Items.Count);
        Assert.Equal(10, result.Value.TotalCount);
        Assert.Equal(2, result.Value.TotalPages);
    }

    [Fact]
    public async Task Handle_SortDirectionAscending_ReturnsOldestFirst()
    {
        await using var context = CreateContext();
        var baseTime = DateTimeOffset.UtcNow;
        context.FeedItems.Add(CreateFeedItem("Newest", createdAt: baseTime));
        context.FeedItems.Add(CreateFeedItem("Middle", createdAt: baseTime.AddMinutes(-5)));
        context.FeedItems.Add(CreateFeedItem("Oldest", createdAt: baseTime.AddMinutes(-10)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query { SortDirection = "asc" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Oldest", result.Value!.Items[0].Title);
    }

    [Fact]
    public async Task Handle_CombinedFilters_AppliesAll()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem("Match", type: FeedItemType.TrendAlert, priority: FeedItemPriority.High, isRead: false));
        context.FeedItems.Add(CreateFeedItem("Wrong Type", type: FeedItemType.AgentDraft, priority: FeedItemPriority.High, isRead: false));
        context.FeedItems.Add(CreateFeedItem("Wrong Priority", type: FeedItemType.TrendAlert, priority: FeedItemPriority.Low, isRead: false));
        context.FeedItems.Add(CreateFeedItem("Wrong Read", type: FeedItemType.TrendAlert, priority: FeedItemPriority.High, isRead: true));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query
        {
            Type = FeedItemType.TrendAlert,
            Priority = FeedItemPriority.High,
            IsRead = false
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Match", result.Value.Items[0].Title);
    }
}
