using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Tests.Features.Scheduling;

public class ContentSchedulerTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<IWorkflowEngine> _workflowEngine = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
    private readonly ContentScheduler _scheduler;
    private readonly DateTimeOffset _now = new(2026, 3, 13, 12, 0, 0, TimeSpan.Zero);

    public ContentSchedulerTests()
    {
        _dateTimeProvider.Setup(x => x.UtcNow).Returns(_now);
        _scheduler = new ContentScheduler(_dbContext.Object, _workflowEngine.Object, _dateTimeProvider.Object);
    }

    private void SetupContents(params ContentEntity[] contents)
    {
        var mockDbSet = contents.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
    }

    private static ContentEntity CreateInStatus(ContentStatus status)
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Test body");
        if (content.Status == status) return content;
        var path = status switch
        {
            ContentStatus.Review => new[] { ContentStatus.Review },
            ContentStatus.Approved => new[] { ContentStatus.Review, ContentStatus.Approved },
            ContentStatus.Scheduled => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled },
            _ => Array.Empty<ContentStatus>()
        };
        foreach (var s in path) content.TransitionTo(s);
        return content;
    }

    [Fact]
    public async Task ScheduleAsync_SetsScheduledAtAndTransitions()
    {
        var content = CreateInStatus(ContentStatus.Approved);
        var scheduledAt = _now.AddHours(1);
        SetupContents(content);
        _workflowEngine.Setup(x => x.TransitionAsync(
                content.Id, ContentStatus.Scheduled, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var result = await _scheduler.ScheduleAsync(content.Id, scheduledAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(scheduledAt, content.ScheduledAt);
    }

    [Fact]
    public async Task ScheduleAsync_FailsWhenContentNotApproved()
    {
        var content = CreateInStatus(ContentStatus.Draft);
        SetupContents(content);

        var result = await _scheduler.ScheduleAsync(content.Id, _now.AddHours(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ScheduleAsync_FailsWhenScheduledAtInPast()
    {
        var content = CreateInStatus(ContentStatus.Approved);
        SetupContents(content);

        var result = await _scheduler.ScheduleAsync(content.Id, _now.AddHours(-1));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task RescheduleAsync_UpdatesScheduledAt()
    {
        var content = CreateInStatus(ContentStatus.Scheduled);
        content.ScheduledAt = _now.AddHours(1);
        var newTime = _now.AddHours(2);
        SetupContents(content);

        var result = await _scheduler.RescheduleAsync(content.Id, newTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(newTime, content.ScheduledAt);
    }

    [Fact]
    public async Task RescheduleAsync_FailsWhenContentNotScheduled()
    {
        var content = CreateInStatus(ContentStatus.Approved);
        SetupContents(content);

        var result = await _scheduler.RescheduleAsync(content.Id, _now.AddHours(2));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task RescheduleAsync_FailsWhenNewTimeInPast()
    {
        var content = CreateInStatus(ContentStatus.Scheduled);
        SetupContents(content);

        var result = await _scheduler.RescheduleAsync(content.Id, _now.AddHours(-1));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task CancelAsync_TransitionsScheduledToApproved()
    {
        var content = CreateInStatus(ContentStatus.Scheduled);
        content.ScheduledAt = _now.AddHours(1);
        SetupContents(content);
        _workflowEngine.Setup(x => x.TransitionAsync(
                content.Id, ContentStatus.Approved, "Schedule cancelled", ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var result = await _scheduler.CancelAsync(content.Id);

        Assert.True(result.IsSuccess);
        Assert.Null(content.ScheduledAt);
    }

    [Fact]
    public async Task CancelAsync_FailsWhenContentNotScheduled()
    {
        var content = CreateInStatus(ContentStatus.Draft);
        SetupContents(content);

        var result = await _scheduler.CancelAsync(content.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }
}
