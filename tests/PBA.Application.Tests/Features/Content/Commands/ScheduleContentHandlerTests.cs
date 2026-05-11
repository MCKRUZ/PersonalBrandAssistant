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

public class ScheduleContentHandlerTests
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
    public async Task Handle_FromApproved_SchedulesAndTransitions()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Body", Status = ContentStatus.Approved };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var scheduledAt = DateTimeOffset.UtcNow.AddHours(2);
        _scheduler.Setup(s => s.SchedulePublish(content.Id, scheduledAt)).Returns("job-123");

        var handler = new ScheduleContent.Handler(context, _scheduler.Object);
        var result = await handler.Handle(new ScheduleContent.Command(content.Id, scheduledAt), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Scheduled, reloaded!.Status);
        Assert.Equal(scheduledAt, reloaded.ScheduledAt);
        Assert.Equal("job-123", reloaded.HangfireJobId);
        _scheduler.Verify(s => s.SchedulePublish(content.Id, scheduledAt), Times.Once);
    }

    [Fact]
    public async Task Handle_FromIdea_ReturnsFailure()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Idea };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new ScheduleContent.Handler(context, _scheduler.Object);
        var result = await handler.Handle(
            new ScheduleContent.Command(content.Id, DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        _scheduler.Verify(s => s.SchedulePublish(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonexistentContent_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new ScheduleContent.Handler(context, _scheduler.Object);
        var result = await handler.Handle(
            new ScheduleContent.Command(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
