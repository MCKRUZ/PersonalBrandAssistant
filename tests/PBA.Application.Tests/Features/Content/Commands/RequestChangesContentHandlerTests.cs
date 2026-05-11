using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class RequestChangesContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_FromReview_TransitionsToDraft()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Text", Status = ContentStatus.Review };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new RequestChangesContent.Handler(context);
        var result = await handler.Handle(new RequestChangesContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Draft, reloaded!.Status);
    }

    [Fact]
    public async Task Handle_FromReview_ClearsScheduledFields()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Test",
            Body = "Text",
            Status = ContentStatus.Review,
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            HangfireJobId = "job-123"
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new RequestChangesContent.Handler(context);
        await handler.Handle(new RequestChangesContent.Command(content.Id), CancellationToken.None);

        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Null(reloaded!.ScheduledAt);
        Assert.Null(reloaded.HangfireJobId);
    }

    [Fact]
    public async Task Handle_NonexistentContent_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new RequestChangesContent.Handler(context);
        var result = await handler.Handle(new RequestChangesContent.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
