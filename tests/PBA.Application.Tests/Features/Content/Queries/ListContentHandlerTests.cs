using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Queries;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Queries;

public class ListContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ContentEntity CreateContent(
        string title = "Test Content",
        ContentStatus status = ContentStatus.Draft,
        Platform platform = Platform.Blog,
        ContentType contentType = ContentType.Blog,
        Guid? parentContentId = null,
        bool isDeleted = false,
        DateTimeOffset? updatedAt = null)
    {
        return new ContentEntity
        {
            Title = title,
            Status = status,
            PrimaryPlatform = platform,
            ContentType = contentType,
            ParentContentId = parentContentId,
            IsDeleted = isDeleted,
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task Handle_DefaultQuery_ReturnsPaginatedResults()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 25; i++)
            context.Contents.Add(CreateContent(title: $"Content {i}"));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value!.Items.Count);
        Assert.Equal(25, result.Value.TotalCount);
        Assert.Equal(2, result.Value.TotalPages);
    }

    [Fact]
    public async Task Handle_StatusFilter_ReturnsMatchingOnly()
    {
        await using var context = CreateContext();
        context.Contents.Add(CreateContent(status: ContentStatus.Draft));
        context.Contents.Add(CreateContent(status: ContentStatus.Draft));
        context.Contents.Add(CreateContent(status: ContentStatus.Published));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query { Status = ContentStatus.Draft }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, i => Assert.Equal(ContentStatus.Draft, i.Status));
    }

    [Fact]
    public async Task Handle_PlatformFilter_ReturnsMatchingOnly()
    {
        await using var context = CreateContext();
        context.Contents.Add(CreateContent(platform: Platform.Blog));
        context.Contents.Add(CreateContent(platform: Platform.LinkedIn));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query { Platform = Platform.Blog }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(Platform.Blog, result.Value.Items[0].PrimaryPlatform);
    }

    [Fact]
    public async Task Handle_ContentTypeFilter_ReturnsMatchingOnly()
    {
        await using var context = CreateContext();
        context.Contents.Add(CreateContent(contentType: ContentType.Blog));
        context.Contents.Add(CreateContent(contentType: ContentType.Tweet));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query { ContentType = ContentType.Blog }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(ContentType.Blog, result.Value.Items[0].ContentType);
    }

    [Fact]
    public async Task Handle_DateRangeFilter_ReturnsWithinRange()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Contents.Add(CreateContent(title: "Old", updatedAt: now.AddDays(-10)));
        context.Contents.Add(CreateContent(title: "InRange", updatedAt: now.AddDays(-3)));
        context.Contents.Add(CreateContent(title: "Future", updatedAt: now.AddDays(5)));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query
        {
            DateFrom = now.AddDays(-5),
            DateTo = now.AddDays(-1)
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("InRange", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task Handle_SearchText_MatchesTitleCaseInsensitive()
    {
        await using var context = CreateContext();
        context.Contents.Add(CreateContent(title: "Using Claude for AI"));
        context.Contents.Add(CreateContent(title: "Using claude efficiently"));
        context.Contents.Add(CreateContent(title: "Other topic"));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query { Search = "claude" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task Handle_ExcludesChildContent()
    {
        await using var context = CreateContext();
        var parent = CreateContent(title: "Parent");
        context.Contents.Add(parent);
        await context.SaveChangesAsync();

        context.Contents.Add(CreateContent(title: "Child", parentContentId: parent.Id));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Parent", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task Handle_ExcludesSoftDeletedContent()
    {
        await using var context = CreateContext();
        context.Contents.Add(CreateContent(title: "Active"));
        context.Contents.Add(CreateContent(title: "Deleted", isDeleted: true));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Active", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task Handle_OrdersByUpdatedAtDescending()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Contents.Add(CreateContent(title: "Oldest", updatedAt: now.AddDays(-3)));
        context.Contents.Add(CreateContent(title: "Newest", updatedAt: now));
        context.Contents.Add(CreateContent(title: "Middle", updatedAt: now.AddDays(-1)));
        await context.SaveChangesAsync();

        var handler = new ListContent.Handler(context);
        var result = await handler.Handle(new ListContent.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Newest", result.Value!.Items[0].Title);
    }
}
