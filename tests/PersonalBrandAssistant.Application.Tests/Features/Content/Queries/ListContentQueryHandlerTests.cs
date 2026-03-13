using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;
using PersonalBrandAssistant.Domain.Enums;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Queries;

public class ListContentQueryHandlerTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();

    private ListContentQueryHandler CreateHandler(List<ContentEntity> data)
    {
        var mockDbSet = data.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
        return new ListContentQueryHandler(_dbContext.Object);
    }

    [Fact]
    public async Task Handle_EmptyDatabase_ReturnsEmptyResult()
    {
        var handler = CreateHandler([]);

        var result = await handler.Handle(new ListContentQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.False(result.Value.HasMore);
    }

    [Fact]
    public async Task Handle_WithItems_ReturnsPagedResult()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => ContentEntity.Create(ContentType.BlogPost, $"Body {i}"))
            .ToList();
        var handler = CreateHandler(items);

        var result = await handler.Handle(new ListContentQuery(PageSize: 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Items.Count);
        Assert.False(result.Value.HasMore);
    }

    [Fact]
    public async Task Handle_PageSizeExceeded_TruncatesAndSetsHasMore()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => ContentEntity.Create(ContentType.BlogPost, $"Body {i}"))
            .ToList();
        var handler = CreateHandler(items);

        var result = await handler.Handle(new ListContentQuery(PageSize: 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.True(result.Value.HasMore);
        Assert.NotNull(result.Value.Cursor);
    }

    [Fact]
    public async Task Handle_FilterByContentType_FiltersCorrectly()
    {
        var items = new List<ContentEntity>
        {
            ContentEntity.Create(ContentType.BlogPost, "Blog"),
            ContentEntity.Create(ContentType.SocialPost, "Tweet"),
            ContentEntity.Create(ContentType.BlogPost, "Blog2"),
        };
        var handler = CreateHandler(items);

        var result = await handler.Handle(
            new ListContentQuery(ContentType: ContentType.SocialPost), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(ContentType.SocialPost, result.Value.Items[0].ContentType);
    }

    [Fact]
    public async Task Handle_FilterByStatus_FiltersCorrectly()
    {
        var draft = ContentEntity.Create(ContentType.BlogPost, "Draft");
        var review = ContentEntity.Create(ContentType.BlogPost, "Review");
        review.TransitionTo(ContentStatus.Review);
        var handler = CreateHandler([draft, review]);

        var result = await handler.Handle(
            new ListContentQuery(Status: ContentStatus.Review), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(ContentStatus.Review, result.Value.Items[0].Status);
    }

    [Fact]
    public async Task Handle_PageSizeCappedAt50()
    {
        var items = Enumerable.Range(0, 55)
            .Select(i => ContentEntity.Create(ContentType.BlogPost, $"Body {i}"))
            .ToList();
        var handler = CreateHandler(items);

        var result = await handler.Handle(new ListContentQuery(PageSize: 100), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(50, result.Value!.Items.Count);
        Assert.True(result.Value.HasMore);
    }

    [Fact]
    public async Task Handle_NoCursor_ReturnsNull()
    {
        var items = new List<ContentEntity>
        {
            ContentEntity.Create(ContentType.BlogPost, "Body"),
        };
        var handler = CreateHandler(items);

        var result = await handler.Handle(new ListContentQuery(PageSize: 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Cursor);
        Assert.False(result.Value.HasMore);
    }
}
