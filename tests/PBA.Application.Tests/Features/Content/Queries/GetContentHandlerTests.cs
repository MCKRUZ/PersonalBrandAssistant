using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Queries;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Queries;

public class GetContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_ExistingContent_ReturnsDetailWithPlatformPublishes()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test" };
        context.Contents.Add(content);
        context.ContentPlatformPublishes.Add(new ContentPlatformPublish
        {
            ContentId = content.Id,
            Platform = Platform.LinkedIn,
            Status = PublishStatus.Published,
            PublishedUrl = "https://linkedin.com/post/1"
        });
        context.ContentPlatformPublishes.Add(new ContentPlatformPublish
        {
            ContentId = content.Id,
            Platform = Platform.Twitter,
            Status = PublishStatus.Pending
        });
        await context.SaveChangesAsync();

        var handler = new GetContent.Handler(context);
        var result = await handler.Handle(new GetContent.Query(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.PlatformPublishes.Count);
    }

    [Fact]
    public async Task Handle_ExistingContent_ReturnsDetailWithChildren()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity { Title = "Parent Post" };
        context.Contents.Add(parent);
        context.Contents.Add(new ContentEntity
        {
            Title = "LinkedIn Version",
            ParentContentId = parent.Id,
            PrimaryPlatform = Platform.LinkedIn
        });
        context.Contents.Add(new ContentEntity
        {
            Title = "Twitter Version",
            ParentContentId = parent.Id,
            PrimaryPlatform = Platform.Twitter
        });
        await context.SaveChangesAsync();

        var handler = new GetContent.Handler(context);
        var result = await handler.Handle(new GetContent.Query(parent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Children.Count);
    }

    [Fact]
    public async Task Handle_NonExistentId_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new GetContent.Handler(context);
        var result = await handler.Handle(new GetContent.Query(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }

    [Fact]
    public async Task Handle_ExcludesSoftDeletedChildren()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity { Title = "Parent" };
        context.Contents.Add(parent);
        context.Contents.Add(new ContentEntity
        {
            Title = "Active Child",
            ParentContentId = parent.Id
        });
        context.Contents.Add(new ContentEntity
        {
            Title = "Deleted Child",
            ParentContentId = parent.Id,
            IsDeleted = true
        });
        await context.SaveChangesAsync();

        var handler = new GetContent.Handler(context);
        var result = await handler.Handle(new GetContent.Query(parent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Children);
        Assert.Equal("Active Child", result.Value.Children[0].Title);
    }
}
