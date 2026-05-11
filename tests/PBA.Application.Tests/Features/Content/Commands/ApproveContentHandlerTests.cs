using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class ApproveContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_FromDraft_TransitionsToApproved()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Has body", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new ApproveContent.Handler(context);
        var result = await handler.Handle(new ApproveContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Approved, reloaded!.Status);
    }

    [Fact]
    public async Task Handle_FromReview_TransitionsToApproved()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Has body", Status = ContentStatus.Review };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new ApproveContent.Handler(context);
        var result = await handler.Handle(new ApproveContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Approved, reloaded!.Status);
    }

    [Fact]
    public async Task Handle_FromIdea_ReturnsFailure()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Idea };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new ApproveContent.Handler(context);
        var result = await handler.Handle(new ApproveContent.Command(content.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NonexistentContent_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new ApproveContent.Handler(context);
        var result = await handler.Handle(new ApproveContent.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
