using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Features.Content.Queries.GetContent;
using PersonalBrandAssistant.Domain.Enums;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Queries;

public class GetContentQueryHandlerTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();

    private GetContentQueryHandler CreateHandler(List<ContentEntity> data)
    {
        var mockDbSet = data.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
        return new GetContentQueryHandler(_dbContext.Object);
    }

    [Fact]
    public async Task Handle_ContentExists_ReturnsSuccess()
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Body", "Title");
        var handler = CreateHandler([content]);

        var result = await handler.Handle(new GetContentQuery(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(content.Id, result.Value!.Id);
    }

    [Fact]
    public async Task Handle_ContentNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler([]);

        var result = await handler.Handle(new GetContentQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_MultipleContents_ReturnsCorrectOne()
    {
        var content1 = ContentEntity.Create(ContentType.BlogPost, "Body1");
        var content2 = ContentEntity.Create(ContentType.SocialPost, "Body2");
        var handler = CreateHandler([content1, content2]);

        var result = await handler.Handle(new GetContentQuery(content2.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentType.SocialPost, result.Value!.ContentType);
    }
}
