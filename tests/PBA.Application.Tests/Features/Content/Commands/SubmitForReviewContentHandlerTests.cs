using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class SubmitForReviewContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_DraftWithBody_TransitionsToReview()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some text", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new SubmitForReviewContent.Handler(context);
        var result = await handler.Handle(new SubmitForReviewContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Review, reloaded!.Status);
    }

    [Fact]
    public async Task Handle_DraftWithEmptyBody_ReturnsFailure()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new SubmitForReviewContent.Handler(context);
        var result = await handler.Handle(new SubmitForReviewContent.Command(content.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NonexistentContent_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new SubmitForReviewContent.Handler(context);
        var result = await handler.Handle(new SubmitForReviewContent.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
