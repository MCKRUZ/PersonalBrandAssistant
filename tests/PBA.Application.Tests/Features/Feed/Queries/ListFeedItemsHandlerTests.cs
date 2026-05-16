using PBA.Application.Features.Feed.Queries;
using PBA.Domain.Enums;
using Xunit;
using static PBA.Application.Tests.Features.Feed.FeedTestHelpers;

namespace PBA.Application.Tests.Features.Feed.Queries;

public class ListFeedItemsHandlerTests
{
    [Fact]
    public async Task Handle_DefaultQuery_ReturnsPaginatedResultsSortedByCreatedAtDesc()
    {
        await using var context = CreateContext();
        var baseTime = DateTimeOffset.UtcNow;
        for (var i = 0; i < 25; i++)
            context.FeedItems.Add(CreateFeedItem(createdAt: baseTime.AddMinutes(-i)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var page = result.Value!;
        Assert.Equal(20, page.Items.Count);
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(baseTime, page.Items[0].CreatedAt);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectTotalCountAndPageMetadata()
    {
        await using var context = CreateContext();
        var baseTime = DateTimeOffset.UtcNow;
        for (var i = 0; i < 25; i++)
            context.FeedItems.Add(CreateFeedItem(createdAt: baseTime.AddMinutes(-i)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query { Page = 1, PageSize = 10 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var page = result.Value!;
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
    }

    [Fact]
    public async Task Handle_FiltersByType_WhenSpecified()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.SystemNotification));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(
            new ListFeedItems.Query { Type = FeedItemType.TrendAlert }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, item => Assert.Equal(FeedItemType.TrendAlert, item.Type));
    }

    [Fact]
    public async Task Handle_FiltersByPriority_WhenSpecified()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(priority: FeedItemPriority.High));
        context.FeedItems.Add(CreateFeedItem(priority: FeedItemPriority.High));
        context.FeedItems.Add(CreateFeedItem(priority: FeedItemPriority.Normal));
        context.FeedItems.Add(CreateFeedItem(priority: FeedItemPriority.Low));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(
            new ListFeedItems.Query { Priority = FeedItemPriority.High }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, item => Assert.Equal(FeedItemPriority.High, item.Priority));
    }

    [Fact]
    public async Task Handle_FiltersByIsReadStatus_WhenSpecified()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(isRead: false));
        context.FeedItems.Add(CreateFeedItem(isRead: false));
        context.FeedItems.Add(CreateFeedItem(isRead: false));
        context.FeedItems.Add(CreateFeedItem(isRead: true));
        context.FeedItems.Add(CreateFeedItem(isRead: true));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(
            new ListFeedItems.Query { IsRead = false }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TotalCount);
        Assert.All(result.Value.Items, item => Assert.False(item.IsRead));
    }

    [Fact]
    public async Task Handle_ExcludesExpiredItemsByDefault()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem());
        context.FeedItems.Add(CreateFeedItem(expiresAt: DateTimeOffset.UtcNow.AddDays(1)));
        context.FeedItems.Add(CreateFeedItem(expiresAt: DateTimeOffset.UtcNow.AddHours(-1)));
        context.FeedItems.Add(CreateFeedItem(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    [Fact]
    public async Task Handle_IncludesExpiredItems_WhenIncludeExpiredTrue()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem());
        context.FeedItems.Add(CreateFeedItem(expiresAt: DateTimeOffset.UtcNow.AddDays(1)));
        context.FeedItems.Add(CreateFeedItem(expiresAt: DateTimeOffset.UtcNow.AddHours(-1)));
        context.FeedItems.Add(CreateFeedItem(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(
            new ListFeedItems.Query { IncludeExpired = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.TotalCount);
    }

    [Fact]
    public async Task Handle_EmptyDatabase_ReturnsEmptyPage()
    {
        await using var context = CreateContext();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(new ListFeedItems.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var page = result.Value!;
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public async Task Handle_RespectsPageSizeLimit()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 10; i++)
            context.FeedItems.Add(CreateFeedItem());
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(
            new ListFeedItems.Query { PageSize = 5 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Items.Count);
        Assert.Equal(10, result.Value.TotalCount);
        Assert.Equal(2, result.Value.TotalPages);
    }

    [Fact]
    public async Task Handle_AppliesSortDirectionAscending()
    {
        await using var context = CreateContext();
        var baseTime = DateTimeOffset.UtcNow;
        context.FeedItems.Add(CreateFeedItem(createdAt: baseTime));
        context.FeedItems.Add(CreateFeedItem(createdAt: baseTime.AddMinutes(-10)));
        context.FeedItems.Add(CreateFeedItem(createdAt: baseTime.AddMinutes(-5)));
        await context.SaveChangesAsync();

        var handler = new ListFeedItems.Handler(context);
        var result = await handler.Handle(
            new ListFeedItems.Query { SortDirection = "asc" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;
        Assert.Equal(3, items.Count);
        Assert.Equal(baseTime.AddMinutes(-10), items[0].CreatedAt);
        Assert.Equal(baseTime.AddMinutes(-5), items[1].CreatedAt);
        Assert.Equal(baseTime, items[2].CreatedAt);
    }
}
