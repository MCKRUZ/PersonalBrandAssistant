using Microsoft.EntityFrameworkCore;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class UnscheduleContentHandlerTests
{
    private readonly Mock<IContentScheduler> _scheduler = new();

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_FromScheduled_UnschedulesAndTransitions()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Test",
            Body = "Body",
            Status = ContentStatus.Scheduled,
            HangfireJobId = "job-456",
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(2)
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UnscheduleContent.Handler(context, _scheduler.Object);
        var result = await handler.Handle(new UnscheduleContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Approved, reloaded!.Status);
        Assert.Null(reloaded.HangfireJobId);
        Assert.Null(reloaded.ScheduledAt);
        _scheduler.Verify(s => s.CancelScheduledPublish("job-456"), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNullHangfireJobId_DoesNotCallCancel()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Test",
            Body = "Body",
            Status = ContentStatus.Scheduled,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(2)
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UnscheduleContent.Handler(context, _scheduler.Object);
        var result = await handler.Handle(new UnscheduleContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _scheduler.Verify(s => s.CancelScheduledPublish(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_FromDraft_ReturnsFailure()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Body", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UnscheduleContent.Handler(context, _scheduler.Object);
        var result = await handler.Handle(new UnscheduleContent.Command(content.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        _scheduler.Verify(s => s.CancelScheduledPublish(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonexistentContent_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new UnscheduleContent.Handler(context, _scheduler.Object);
        var result = await handler.Handle(new UnscheduleContent.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
